import { useEffect, useRef } from 'react'

/**
 * Regular polling: runs `task` every `intervalMs`. It skips ticks while the browser tab is hidden, runs once as soon as
 * the tab becomes visible again, never overlaps itself, and aborts an in-flight request on unmount.
 * The task owns its error handling (a failed poll should keep showing the last good data).
 */
export function usePolling(task: (signal: AbortSignal) => Promise<void>, intervalMs: number, enabled = true) {
  const taskRef = useRef(task)
  useEffect(() => {
    taskRef.current = task
  })

  useEffect(() => {
    if (!enabled) return
    let running = false
    let ctrl: AbortController | null = null

    const tick = async () => {
      if (document.hidden || running) return
      running = true
      ctrl = new AbortController()
      try {
        await taskRef.current(ctrl.signal)
      } finally {
        running = false
      }
    }

    const timer = setInterval(() => void tick(), intervalMs)
    const onVisibility = () => {
      if (!document.hidden) void tick()
    }
    document.addEventListener('visibilitychange', onVisibility)

    return () => {
      clearInterval(timer)
      document.removeEventListener('visibilitychange', onVisibility)
      ctrl?.abort()
    }
  }, [intervalMs, enabled])
}
