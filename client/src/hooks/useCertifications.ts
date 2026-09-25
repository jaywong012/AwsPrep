import { useCallback, useEffect, useRef, useState } from 'react'
import { api } from '../api'
import type { Certification } from '../api/types'

const STORAGE_KEY = 'awscert.selectedCode'

/**
 * The certification catalogue, loaded once per signed-in account.
 *
 * Keyed on the account rather than on mount. Two things fall out of that: nothing is fetched
 * while the sign-in screen is up, and signing in - or signing in as somebody else without
 * reloading the tab - refetches exactly once.
 *
 * @param userId the signed-in account, or null when nobody is.
 */
export function useCertifications(userId: string | null) {
  const [certifications, setCertifications] = useState<Certification[]>([])
  const [selectedCode, setSelectedCode] = useState<string>(
    () => localStorage.getItem(STORAGE_KEY) ?? 'AIF-C01',
  )
  // Starts true so the first render of a signed-in app shows a skeleton rather than "no
  // certifications", and is set false by reload() even when there is nothing to load.
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const reload = useCallback(async () => {
    setLoading(true)
    setError(null)
    try {
      const data = await api.certifications.list()
      setCertifications(data)
      if (data.length > 0 && !data.some((c) => c.code === selectedCode)) {
        setSelectedCode(data[0].code)
      }
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to load certifications')
    } finally {
      setLoading(false)
    }
  }, [selectedCode])

  // A ref rather than state: it is set before the effect can run a second time, so StrictMode's
  // double invocation cannot fetch twice the way a state flag would. reload() stays exported so
  // pages can refresh counts after generating or deleting questions - re-running this effect on
  // every change of reload would loop, since reload closes over selectedCode.
  const loadedFor = useRef<string | null>(null)
  useEffect(() => {
    if (!userId || loadedFor.current === userId) return
    loadedFor.current = userId
    void reload()
  }, [userId, reload])

  const select = useCallback((code: string) => {
    setSelectedCode(code)
    localStorage.setItem(STORAGE_KEY, code)
  }, [])

  const selected = certifications.find((c) => c.code === selectedCode) ?? null

  return { certifications, selected, selectedCode, select, loading, error, reload }
}
