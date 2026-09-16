import { useCallback, useEffect, useState } from 'react'
import { api } from '../api'
import type { Certification } from '../api/types'

const STORAGE_KEY = 'awscert.selectedCode'

export function useCertifications() {
  const [certifications, setCertifications] = useState<Certification[]>([])
  const [selectedCode, setSelectedCode] = useState<string>(
    () => localStorage.getItem(STORAGE_KEY) ?? 'AIF-C01',
  )
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

  // Loads once on mount. reload() is exposed so pages can refresh counts after
  // generating or deleting questions; re-running on every reload identity change
  // would loop, since reload closes over selectedCode.
  const [mounted, setMounted] = useState(false)
  useEffect(() => {
    if (mounted) return
    setMounted(true)
    void reload()
  }, [mounted, reload])

  const select = useCallback((code: string) => {
    setSelectedCode(code)
    localStorage.setItem(STORAGE_KEY, code)
  }, [])

  const selected = certifications.find((c) => c.code === selectedCode) ?? null

  return { certifications, selected, selectedCode, select, loading, error, reload }
}
