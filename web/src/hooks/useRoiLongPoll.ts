import { useEffect, useRef, useState } from 'react'
import { pollRoiChanges } from '../api/logs'
import type { RoiData } from '../types'

export type LiveStatus = 'connecting' | 'live' | 'reconnecting'

const RETRY_DELAY_MS = 5000

/**
 * Long polling: keeps one request open asking "anything newer than version N?". The server answers as soon as the
 * data changes (or with 204 after ~25s), and the loop immediately asks again. No websockets or SignalR.
 *
 * - `getVersion` supplies the version the UI currently shows (null until the first load finishes).
 * - The loop pauses while the tab is hidden and resumes when it is visible again.
 * - After an error it waits a few seconds before retrying, and reports "reconnecting".
 */
export function useRoiLongPoll(
  automationId: string,
  getVersion: () => number | null,
  onChange: (roi: RoiData) => void,
): LiveStatus {
  const [status, setStatus] = useState<LiveStatus>('connecting')
  const getVersionRef = useRef(getVersion)
  const onChangeRef = useRef(onChange)
  useEffect(() => {
    getVersionRef.current = getVersion
    onChangeRef.current = onChange
  })

  useEffect(() => {
    let stopped = false
    let ctrl: AbortController | null = null
    let wake: (() => void) | null = null

    // Both waits can be cut short by `wake` (tab became visible, or the component unmounted).
    const sleep = (ms: number) =>
      new Promise<void>((resolve) => {
        const timer = setTimeout(resolve, ms)
        wake = () => {
          clearTimeout(timer)
          resolve()
        }
      })
    const waitUntilVisible = () =>
      new Promise<void>((resolve) => {
        wake = resolve
      })

    const onVisibility = () => {
      if (document.hidden) ctrl?.abort()
      else wake?.()
    }
    document.addEventListener('visibilitychange', onVisibility)

    void (async () => {
      while (!stopped) {
        if (document.hidden) {
          await waitUntilVisible()
          continue
        }
        const since = getVersionRef.current()
        if (since === null) {
          await sleep(300)
          continue
        }

        ctrl = new AbortController()
        try {
          const roi = await pollRoiChanges(automationId, since, ctrl.signal)
          if (stopped) break
          setStatus('live')
          if (roi) onChangeRef.current(roi)
        } catch (e) {
          if (stopped) break
          if (e instanceof DOMException && e.name === 'AbortError') continue // tab hidden; the loop pauses at the top
          setStatus('reconnecting')
          await sleep(RETRY_DELAY_MS)
        }
      }
    })()

    return () => {
      stopped = true
      ctrl?.abort()
      wake?.()
      document.removeEventListener('visibilitychange', onVisibility)
    }
  }, [automationId])

  return status
}
