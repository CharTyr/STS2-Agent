"""Tests for MCP intra-frame compact slimming, session static dedup, and plan step trimming (#233)."""

from __future__ import annotations

import asyncio
from copy import deepcopy
import json
import unittest
from typing import Any

from sts2_mcp.server import create_server
from sts2_mcp.state_diet import SessionStateDedup, slim_compact_state


def _bloated_raw_state(
    *,
    run_id: str = "run_alpha",
    extra_card: bool = False,
    extra_relic: bool = False,
    extra_glossary: str | None = None,
) -> dict[str, Any]:
    deck = [
        {"line": "打击*5 [1] 造成6点伤害。", "card_ids": ["STRIKE_IRONCLAD"], "keywords": [], "mods": []},
        {"line": "防御*4 [1] 获得5点格挡。", "card_ids": ["DEFEND_IRONCLAD"], "keywords": ["格挡"], "mods": []},
        {"line": "痛击*1 [2] 造成8点伤害，给予2层易伤。", "card_ids": ["BASH"], "keywords": ["易伤"], "mods": []},
        {"line": "耸肩无视*2 [1] 获得8点格挡，抽1张牌。", "card_ids": ["SHRUG_IT_OFF"], "keywords": ["格挡"], "mods": []},
        {"line": "发炎*1 [1] 获得2点力量。", "card_ids": ["INFLAME"], "keywords": ["力量"], "mods": []},
    ]
    if extra_card:
        deck.append({"line": "顺劈斩*1 [1] 对所有敌人造成8点伤害。", "card_ids": ["CLEAVE"], "keywords": [], "mods": []})

    relics = ["燃烧之血", "金刚杵", "锚"]
    relic_ids = ["BURNING_BLOOD", "VAJRA", "ANCHOR"]
    relic_descs = [
        "在战斗结束时，回复6点生命。",
        "在每场战斗开始时，获得1点力量。",
        "在每场战斗开始时，获得10点格挡。",
    ]
    if extra_relic:
        relics.append("准备背包")
        relic_ids.append("BAG_OF_PREP")
        relic_descs.append("在每场战斗开始时，额外抽2张牌。")

    glossary = {
        "格挡": "直到下回合开始前，抵挡等量的伤害。",
        "易伤": "受到来自攻击的伤害增加50%。",
        "力量": "每点力量使攻击伤害增加1点。",
        "虚弱": "攻击造成的伤害减少25%。",
    }
    if extra_glossary:
        glossary[extra_glossary] = "通过卡牌获得的格挡减少25%。"

    hand = [
        {
            "i": idx,
            "cid": 10 + idx,
            "card_id": "STRIKE_IRONCLAD" if idx % 2 == 0 else "DEFEND_IRONCLAD",
            "line": "打击 [1] 造成6点伤害。" if idx % 2 == 0 else "防御 [1] 获得5点格挡。",
            "playable": True,
            "energy_cost": 1,
            "type": "Attack" if idx % 2 == 0 else "Skill",
            "can_play_result": True,
            "why": None,
            "unplayable_reason": None,
            "unplayable_reason_raw": "",
            "unplayable_preventer_id": None,
            "unplayable_preventer_type": None,
            "star_cost": None,
            "target": "enemy" if idx % 2 == 0 else None,
            "targets": [0] if idx % 2 == 0 else [],
            "afflictions": [],
            "enchantments": [],
        }
        for idx in range(6)
    ]

    agent_view = {
        "version": 13,
        "screen": "COMBAT",
        "in_combat": True,
        "run_id": run_id,
        "turn": 2,
        "available_actions": ["play_card", "end_turn"],
        "shop": None,
        "event": None,
        "reward": None,
        "rest": None,
        "chest": None,
        "selection": None,
        "bundles": None,
        "capstone": None,
        "crystal_sphere": None,
        "map": {
            "open": False,
            "current": "0,4",
            "options": [],
            "nodes": [{"x": f % 4, "y": f, "type": "MONSTER", "children": [{"x": 0, "y": f + 1}]} for f in range(1, 16)],
            "boss": "BOSS_SLIME",
        },
        "run": {
            "character": "铁甲战士",
            "ascension": 10,
            "ascension_effects": [
                {"level": 1, "name": "精英增多", "description": "地图上出现更多精英。"},
                {"level": 2, "name": "敌人更致命", "description": "普通敌人伤害更高。"},
            ],
            "floor": 5,
            "hp": "64/80",
            "gold": 123,
            "deck": deck,
            "relics": relics,
            "relic_ids": relic_ids,
            "relic_stacks": [0, 1, 0],
            "relic_descriptions": relic_descs,
            "players": [
                {
                    "player_id": "1",
                    "is_local": True,
                    "current_hp": 64,
                    "max_hp": 80,
                    "gold": 123,
                    "deck": deepcopy(deck),
                    "relics": deepcopy(relics),
                    "potions": [],
                }
            ],
            "piles": {"draw": deepcopy(deck), "discard": deepcopy(deck), "exhaust": []},
        },
        "combat": {
            "energy": "3/3",
            "card_value_note": "Hand line and values.preview include live player modifiers such as Strength.",
            "action_readiness": {
                "can_use_combat_actions": True,
                "reason": "ok",
                "local_turn_ready": True,
                "debug_noise_flag_1": False,
                "debug_noise_flag_2": "none",
                "debug_noise_flag_3": 0,
            },
            "hand": hand,
            "enemies": [{"i": 0, "enemy_id": "JAW_WORM", "hp": "42/42", "alive": True, "hittable": True}],
        },
        "glossary": glossary,
    }
    return {"screen": "COMBAT", "available_actions": ["play_card", "end_turn"], "agent_view": agent_view}


class _SlimClient:
    def __init__(self) -> None:
        self.raw = _bloated_raw_state()

    def get_state(self) -> dict[str, Any]:
        return deepcopy(self.raw)

    def get_available_actions(self) -> list[dict[str, Any]]:
        return [
            {"name": "play_card", "requires_index": True, "requires_target": True},
            {"name": "end_turn", "requires_index": False, "requires_target": False},
        ]

    def get_decision_snapshot(self) -> dict[str, Any]:
        return {
            "state": deepcopy(self.raw["agent_view"]),
            "available_actions": self.get_available_actions(),
        }

    def execute_action(self, action: str, *, raw_state: bool = False, **kwargs: Any) -> dict[str, Any]:
        self.raw["agent_view"]["run"]["gold"] = int(self.raw["agent_view"]["run"]["gold"]) + 1
        state_out = deepcopy(self.raw) if raw_state else slim_compact_state({**deepcopy(self.raw["agent_view"]), "compact_agent_view": True})
        return {"action": action, "status": "completed", "stable": True, "state": state_out}


class McpStateDietTests(unittest.TestCase):
    def test_intra_frame_and_session_dedup_shrink_payload_and_emit_mutations(self) -> None:
        raw_view = _bloated_raw_state()["agent_view"]
        baseline_bytes = len(json.dumps(raw_view, ensure_ascii=False, separators=(",", ":")))

        dedup = SessionStateDedup()
        first = dedup.apply_to_state(raw_view)
        first_bytes = len(json.dumps(first, ensure_ascii=False, separators=(",", ":")))
        self.assertLessEqual(first_bytes, baseline_bytes * 0.70)
        self.assertNotIn("shop", first)
        self.assertNotIn("nodes", first["map"])
        self.assertNotIn("piles", first["run"])
        self.assertNotIn("deck", first["run"]["players"][0])
        self.assertIn("deck", first["run"])
        self.assertIn("glossary", first)

        second = dedup.apply_to_state(raw_view)
        second_bytes = len(json.dumps(second, ensure_ascii=False, separators=(",", ":")))
        self.assertLessEqual(second_bytes, baseline_bytes * 0.42)
        self.assertTrue(second["run"]["deck_unchanged"])
        self.assertEqual(second["run"]["deck_groups"], 5)
        self.assertNotIn("deck", second["run"])
        self.assertTrue(second["run"]["relic_descriptions_unchanged"])
        self.assertIn("relics", second["run"])
        self.assertIn("relic_stacks", second["run"])
        self.assertTrue(second["glossary_unchanged"])
        self.assertNotIn("card_value_note", second["combat"])

        # Mutate deck, relics, and glossary -> re-emits changed static fields and delta glossary
        mutated = _bloated_raw_state(extra_card=True, extra_relic=True, extra_glossary="脆弱")["agent_view"]
        third = dedup.apply_to_state(mutated)
        self.assertEqual(len(third["run"]["deck"]), 6)
        self.assertEqual(len(third["run"]["relic_descriptions"]), 4)
        self.assertEqual(third["glossary"], {"脆弱": "通过卡牌获得的格挡减少25%。"})
        self.assertTrue(third["glossary_delta"])

        # Empty deck clears cached hash so restored deck is re-emitted instead of suppressed
        empty_deck = _bloated_raw_state()["agent_view"]
        empty_deck["run"]["deck"] = []
        emptied = dedup.apply_to_state(empty_deck)
        self.assertEqual(emptied["run"]["deck"], [])
        restored = dedup.apply_to_state(mutated)
        self.assertIn("deck", restored["run"])

        # full_state=True restores full blocks and reseeds baseline
        full = dedup.apply_to_state(mutated, full_state=True)
        self.assertEqual(len(full["run"]["deck"]), 6)
        self.assertEqual(len(full["glossary"]), 5)
        self.assertNotIn("glossary_delta", full)

        # Switching run_id resets session hashes automatically
        next_run = dedup.apply_to_state(_bloated_raw_state(run_id="run_beta")["agent_view"])
        self.assertIn("deck", next_run["run"])
        self.assertIn("glossary", next_run)

    def test_server_decide_full_state_and_turn_plan_steps_trim(self) -> None:
        from fastmcp import Client

        client = _SlimClient()
        server = create_server(client=client, tool_profile="guided")  # type: ignore[arg-type]
        decide_tool = asyncio.run(server.get_tool("decide"))
        state_tool = asyncio.run(server.get_tool("get_game_state"))
        act_tool = asyncio.run(server.get_tool("act"))

        d1 = decide_tool.fn()
        self.assertIn("deck", d1["state"]["run"])
        self.assertIn("playbook", d1["scene_guidance"])

        d2 = decide_tool.fn()
        self.assertTrue(d2["state"]["run"]["deck_unchanged"])
        self.assertTrue(d2["scene_guidance"]["guidance_unchanged"])

        full = state_tool.fn(full_state=True)
        self.assertIn("deck", full["run"])
        self.assertIn("glossary", full)

        plan_res = act_tool.fn(
            action="play_card",
            card_cid=10,
            target_index=0,
            then=[{"action": "play_card", "card_cid": 11}, {"action": "end_turn"}],
        )
        self.assertEqual(plan_res["plan"]["executed"], 3)
        for step in plan_res["plan"]["steps"]:
            self.assertNotIn("state", step)
        self.assertNotIn("state", plan_res)
        self.assertTrue(plan_res["final_state"]["run"]["deck_unchanged"])
        self.assertEqual(plan_res["final_state"]["run"]["gold"], 126)

        raw_plan = act_tool.fn(
            action="play_card",
            card_cid=10,
            target_index=0,
            raw_state=True,
            then=[{"action": "end_turn"}],
        )
        self.assertIn("state", raw_plan["plan"]["steps"][0])
        self.assertIn("agent_view", raw_plan["final_state"])

        # Separate MCP client sessions receive independent static baselines
        async def _two_clients() -> tuple[dict[str, Any], dict[str, Any], dict[str, Any]]:
            async with Client(server) as c1, Client(server) as c2:
                r1_first = json.loads((await c1.call_tool("get_game_state", {})).content[0].text)
                r1_second = json.loads((await c1.call_tool("get_game_state", {})).content[0].text)
                r2_first = json.loads((await c2.call_tool("get_game_state", {})).content[0].text)
                return r1_first, r1_second, r2_first

        c1_first, c1_second, c2_first = asyncio.run(_two_clients())
        self.assertIn("deck", c1_first["run"])
        self.assertTrue(c1_second["run"]["deck_unchanged"])
        self.assertIn("deck", c2_first["run"])


if __name__ == "__main__":
    unittest.main()
