# Claude Code workflows on this box — RAM limits and safe resume

## Wide agent fan-out can kill the host (WSL2 + Windows)

A multi-agent Workflow that launched 7+ concurrent `general-purpose` research agents (each a full
process with web/gh tooling) exhausted host RAM; Windows froze and the WSL VM died mid-run. This
box cannot sustain a wide parallel fan-out of heavyweight agents.

Mitigation that worked: run thunks in small sequential batches instead of one big `parallel(...)`:

```js
async function batched(thunks, size) {
  const out = []
  for (let i = 0; i < thunks.length; i += size) {
    out.push(...await parallel(thunks.slice(i, i + size)))
  }
  return out
}
// research/attack stages: batched(..., 2); per-item verifier pairs: batched(..., 1)
```

Keep effective concurrency ≤ ~2 heavyweight agents for research-style workflows here.

## Resume cache survives batching edits

Workflow resume (`{scriptPath, resumeFromRunId}`) caches completed `agent()` calls keyed by the
call sequence (prompt + opts). Wrapping the same calls in `batched()` changes only timing, not the
call order or prompts, so a crashed run resumes with all completed agents replayed from cache —
edit orchestration freely, never the agent prompts, if you want cache hits. Check
`<transcriptDir>/journal.jsonl` to see what is already cached before resuming.
