"""The STS2 HTTP envelope, parsed once.

Every response from the mod is one shape -- `{"ok": bool, "request_id": str, "data": ...}` on
success and `{"ok": false, "error": {"code", "message", "retryable", "details"}}` on failure -- and
the client used to re-derive that shape by hand in five places. The hand-written copies had drifted
into two different contracts, and the difference is real rather than accidental:

- **Lenient** (`parse`) is for reads and for the error body of a non-action request. `ok` is
  truthy-tested rather than type-checked and a missing one still fails closed, while a thin error
  object has its missing fields filled in with `unknown_error` / "Request failed.". A GET that
  answers with a slightly off envelope should still hand its data to the caller, and one that does
  not claim success must not be read as success.
- **Strict** (`parse_strict`) is for `POST /action`, where the envelope is the only thing standing
  between a lost response and a replayed card. `ok` must be a boolean and a failure must carry a
  well-typed `code`, `message`, and `retryable`; anything else is `invalid_response` and is never
  retried as if it were a game error.

Keeping both here means the difference is stated once. The strict path is only safe as long as every
action response goes through it -- a second hand-rolled copy of this contract is how an action ends
up replayed.
"""

from __future__ import annotations

import json
from dataclasses import dataclass
from typing import Any


@dataclass(slots=True)
class Sts2ApiError(RuntimeError):
    status_code: int
    code: str
    message: str
    details: Any = None
    retryable: bool = False

    def __str__(self) -> str:
        parts = [f"{self.code}: {self.message}", f"http={self.status_code}"]
        if self.retryable:
            parts.append("retryable=true")
        if self.details is not None:
            parts.append(f"details={json.dumps(self.details, ensure_ascii=False)}")
        return " | ".join(parts)


@dataclass(frozen=True, slots=True)
class EnvelopeError:
    """The `error` object of a failed envelope, normalized to the four fields callers branch on."""

    code: str
    message: str
    details: Any = None
    retryable: bool = False


@dataclass(frozen=True, slots=True)
class Envelope:
    ok: bool
    data: Any
    error: EnvelopeError | None
    raw: dict[str, Any]

    def require_object_data(self) -> dict[str, Any]:
        """The `data` payload, refused when it is not an object.

        A caller that expects fields and receives a list or a scalar would otherwise fail later,
        somewhere with no idea which response was wrong.
        """
        if not isinstance(self.data, dict):
            raise Sts2ApiError(
                status_code=200,
                code="invalid_response",
                message="Server response did not contain an object data payload.",
                details=self.raw,
            )
        return self.data


def _decode_object(
    body: bytes,
    *,
    status_code: int,
    message: str,
    decode_errors: str,
) -> dict[str, Any]:
    """Decode a JSON object body, choosing what a decode failure becomes.

    `decode_errors="raise"` leaves `json.JSONDecodeError` intact, because a truncated *success*
    body is not an API error: the caller has to be able to tell a read that failed from a request
    the mod rejected, and only the latter may be retried as a game outcome. `"wrap"` turns it into
    `invalid_response`, which is what an error body has to become to be reportable at all.
    """
    if decode_errors == "raise":
        payload = json.loads(body.decode("utf-8"))
    else:
        try:
            payload = json.loads(body.decode("utf-8"))
        except (json.JSONDecodeError, UnicodeDecodeError):
            raise Sts2ApiError(status_code=status_code, code="invalid_response", message=message)

    if not isinstance(payload, dict):
        raise Sts2ApiError(
            status_code=status_code,
            code="invalid_response",
            message="Server returned a JSON response that was not an object.",
            details={"response_type": type(payload).__name__},
        )
    return payload


def parse(body: bytes, *, status_code: int, source: str = "Server", decode_errors: str = "wrap") -> Envelope:
    """The lenient contract: reads, and the error body of a non-action request.

    `decode_errors` defaults to wrapping, which is what an error body needs. A read that expects
    data passes `"raise"` so a truncated response keeps its own exception type.
    """
    payload = _decode_object(
        body,
        status_code=status_code,
        message=f"{source} returned a non-JSON error response.",
        decode_errors=decode_errors,
    )

    ok = payload.get("ok", False)
    if not ok:
        raw_error = payload.get("error", {})
        if not isinstance(raw_error, dict):
            raw_error = {}
        return Envelope(
            ok=False,
            data=None,
            error=EnvelopeError(
                code=str(raw_error.get("code") or "unknown_error"),
                message=str(raw_error.get("message") or "Request failed."),
                details=raw_error.get("details"),
                retryable=bool(raw_error.get("retryable", False)),
            ),
            raw=payload,
        )

    return Envelope(ok=True, data=payload.get("data"), error=None, raw=payload)


def parse_strict(body: bytes, *, status_code: int) -> Envelope:
    """The action contract: a malformed envelope is never a retryable game error."""
    payload = _decode_object(
        body,
        status_code=status_code,
        message="Server returned a non-JSON response.",
        decode_errors="wrap",
    )

    ok = payload.get("ok")
    if not isinstance(ok, bool):
        raise Sts2ApiError(
            status_code=status_code,
            code="invalid_response",
            message="Server response field 'ok' was missing or was not a boolean.",
            details={"ok_type": type(ok).__name__},
        )

    if ok:
        return Envelope(ok=True, data=payload.get("data"), error=None, raw=payload)

    raw_error = payload.get("error")
    if not isinstance(raw_error, dict):
        raise Sts2ApiError(
            status_code=status_code,
            code="invalid_response",
            message="Server response field 'error' was missing or was not an object.",
            details={"error_type": type(raw_error).__name__},
        )

    code = raw_error.get("code")
    message = raw_error.get("message")
    retryable = raw_error.get("retryable")
    invalid_field: tuple[str, Any] | None = None
    if not isinstance(code, str) or not code:
        invalid_field = ("code", code)
    elif not isinstance(message, str):
        invalid_field = ("message", message)
    elif not isinstance(retryable, bool):
        invalid_field = ("retryable", retryable)

    if invalid_field is not None:
        field_name, field_value = invalid_field
        raise Sts2ApiError(
            status_code=status_code,
            code="invalid_response",
            message=f"Server response error field '{field_name}' had an invalid schema.",
            details={"field": field_name, "field_type": type(field_value).__name__},
        )

    return Envelope(
        ok=False,
        data=payload.get("data"),
        error=EnvelopeError(
            code=code,
            message=message,
            details=raw_error.get("details"),
            retryable=retryable,
        ),
        raw=payload,
    )
