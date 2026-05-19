# LLM Model Routing

## Configuration

Two model tiers are configured in `appsettings.json` (or environment overrides):

| Key | Default | Purpose |
|-----|---------|---------|
| `LlmRouting:StrongModel` | `gpt-5` | Security-critical reasoning |
| `LlmRouting:LowCostModel` | `gpt-5-mini` | Classification, formatting, deduplication |

The client (OpenAI, Azure OpenAI, Anthropic, Gemini) is selected automatically based on the model name prefix. If `OpenAI:ApiKey` is set, plain OpenAI is preferred over Azure OpenAI for `gpt-*` and `o*` models.

---

## When each tier is used

### ParseStage
| Condition | Model |
|-----------|-------|
| Artifact is an **image** (diagram visual) | Strong |
| Artifact is **text/markdown** | Low-cost |

### NormalizeStage
Always **strong** — both the LLM extraction pass and the canonical model persist pass.

### ClassifyStage
Always **low-cost** — pattern-driven classification, no security reasoning required.

### AnalyzeStage
Determined per threat-modelling method:

| Model | Methods |
|-------|---------|
| **Strong** | `stride`, `tenant_isolation`, `identity_session_delegation`, `ai_llm_threat`, `linddun`, `maestro`, `mitre_attack`, `abuse_case`, `owasp_cumulus`, `owasp_cornucopia`, `supply_chain`, internal security-expert baseline |
| **Low-cost** | `availability_resilience`, `vast`, `pasta`, `octave`, `trike` |

The set is defined in `AnalyzeStage.SecurityCriticalMethods`.

### SynthesizeStage
| Pass | Model |
|------|-------|
| Main synthesis | Strong |
| Deduplication / formatting pass | Low-cost |
| Adversarial review | Strong |

---

## Cost-saving test runs

To route **everything** through the low-cost model (e.g. to verify pipeline flow without spending on GPT-5):

```json
"LlmRouting": {
  "StrongModel": "gpt-5-mini",
  "LowCostModel": "gpt-5-mini"
}
```

This makes both tiers resolve to the same model. The routing logic itself does not change.
