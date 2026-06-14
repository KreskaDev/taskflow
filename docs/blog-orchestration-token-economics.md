# The counterintuitive economics of multi-agent orchestration

*Draft notes for a blog post. Based on a real session: slicing a monolithic product spec into 12 vertical-slice specs with an AI coding agent.*

---

## The hook

Everyone tells you to "use subagents to save tokens." That's wrong — or at least, badly stated.

Multi-agent orchestration **spends more tokens, not fewer.** In my run it burned ~1.22M tokens across 24 agents to produce work I could have hand-written in one pass for a fraction of that.

And yet it was still the cheaper choice. Here's the part nobody explains: *why.*

## The setup

I had a 356-line monolithic product spec (180 tracked requirement IDs) that needed to become 12 independent, shippable "vertical slices" — each its own spec, each traceable back to a single source of truth, no invented content, exact ID accounting.

Two ways to do it:

1. **Inline** — the main agent generates all 12 specs, reviews them, fixes them, one long conversation.
2. **Orchestrated** — a workflow spawns a fresh agent per slice to generate it, a second independent agent to adversarially review and score it (0–100), and loops any slice under 95% through revision.

I ran option 2. Result: all 12 slices passed, scores 96–98, one slice needed a single revision pass. Telemetry: **24 agents, ~1.22M subagent tokens, ~75 minutes wall-clock.**

## The naive take (and why it's incomplete)

"1.22M tokens?! Inline would've been way cheaper — maybe 80K to write twelve specs."

True, *for the task in isolation.* Each spawned agent re-reads the same source files (the vision doc, the constitution, the template, two format exemplars) — pure redundant overhead. And half the agents exist only to review and re-review. In raw aggregate, orchestration is strictly more expensive.

If the story ended when the task ended, inline wins. But it doesn't end there.

## The thing that flips it: the re-billing tax

Here's the mechanism that changes the math.

> **A conversation's context window is re-sent as input on *every single turn.***

When the inline agent generates twelve specs, that ~60–80K tokens of output doesn't just cost you once. It now lives in the context window — and gets **re-transmitted as input tokens on every subsequent turn** of the session. Ten more turns of follow-up work? That's **600–800K tokens** of the same spec content, paid over and over, just to keep it "in view."

The orchestrated version sidesteps this entirely:

- The 1.22M tokens are spent in **ephemeral subagent contexts that are discarded.** They are never re-sent.
- The main conversation only ever receives the **compact result** — a ~2–3K JSON scoreboard.
- So the main window stays lean, and every later turn stays cheap.

**Orchestration converts a recurring tax into a one-time cost.**

## The break-even

Rough mental model:

```
Inline cost      ≈ generation_once  +  (bloat × remaining_turns)
Orchestrated cost ≈ generation  +  redundant_reads  +  review_QA  +  (≈0 × remaining_turns)
```

- For a **trivial task** or a session that **ends right after** → inline wins. No re-billing accrues; the orchestration overhead is dead weight.
- For a **heavy, multi-artifact task** inside a **continuing session** → orchestration wins, and the gap widens with every follow-up turn.

The crossover is real and it arrives fast when the artifacts are large and the session is long.

## The honest caveats (don't skip these)

I almost shipped this as "orchestration saves tokens, full stop." It doesn't, and pretending otherwise is how people get surprised by a bill:

1. **Part of that 1.22M bought quality, not just relocation.** The independent adversarial-review layer is genuine extra spend — but it caught drift a self-review wouldn't have, and produced uniform, verifiable output. That's value, but it *is* usage.
2. **The win is conditional.** It needs the task to be big *and* the session to continue. Miss either and you've just spent more for nothing.
3. **"Total tokens" and "your subscription usage over a session" are different metrics.** Optimize the one that's actually billed to you — which, for a long working session, is dominated by re-billed context, not by one heavy burst.

## What actually makes orchestration work

The economics only pay off if the output is correct enough to not need a do-over. A few principles did the heavy lifting:

- **Tight, self-contained briefs.** Subagents share none of your context. Every prompt carried exact inputs, source file paths, and a *format exemplar* ("mirror this existing file"). That single instruction is why twelve specs came out uniform.
- **Structured returns, not prose.** Reviewers returned `{score, missing_ids, invented_content, feedback}` — machine-checkable, and the model self-corrects against a schema.
- **Adversarial, independent review.** The reviewer's job was to *refute*, with a hard rubric ("one invented item or one missing ID caps the score below 95"). Skepticism beats a friendly second look.
- **Bounded loops.** Iterate-to-threshold with a hard attempt cap, so a stubborn artifact can't loop forever.
- **Scout inline, then fan out.** Discover the work-list yourself; hand the *list* to the workflow. Don't make the orchestration guess its own scope.

## The reusable patterns

The orchestration vocabulary is tiny; the skill is composition. Patterns I keep on tap:

1. **Generate → adversarial review → iterate-to-threshold.** The workhorse for "produce N things that must be correct."
2. **Fan-out finders → dedup → verify.** For audits: several agents search different ways, then a second pass tries to refute each finding. Survivors are real.
3. **Pipeline for sweeps/migrations.** Each item flows through transform→verify independently; wall-clock = slowest single item, not the sum.
4. **Judge panel.** Generate N independent approaches, score with parallel judges, synthesize the winner. Beats one-attempt-iterated when the solution space is wide.

## Takeaway

Don't reach for multi-agent orchestration to "save tokens." Reach for it when:

- you have **many artifacts** to produce or verify,
- **correctness matters** enough to justify independent review, and
- the work happens inside a **session that keeps going.**

Then the real saving shows up — not as a smaller burst, but as a **main context that stays cheap for the rest of the day.** You're not spending less; you're spending *once* instead of *every turn.*

That's the trade. State it honestly and it's an easy call.

---

*Footnotes / things I could expand for the full post:*
- *Concrete numbers table: inline vs orchestrated, with the re-billing multiplier worked out.*
- *Screenshot of the live workflow progress tree.*
- *The one slice that needed revision (93 → 96) and why the thinnest artifact was the riskiest.*
- *Prompt-caching nuance: caching softens re-billing within a 5-min window, but cache misses after gaps / compaction bring the tax back.*
