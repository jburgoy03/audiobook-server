import { useEffect, useState } from 'react'
import { api } from './api/client'
import type { BookSummary } from './api/types'

// Temporary smoke test for the proxy: proves the browser can reach the API.
// Replaced by the real library page next.

function formatHours(seconds: number) {
  return `${(seconds / 3600).toFixed(2)} h`
}

export default function App() {
  const [books, setBooks] = useState<BookSummary[] | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    api
      .books(controller.signal)
      .then(setBooks)
      .catch((e: unknown) => {
        if (!controller.signal.aborted) setError(String(e))
      })
    return () => controller.abort()
  }, [])

  if (error) return <pre>{error}</pre>
  if (!books) return <p>Loading…</p>

  return (
    <main style={{ padding: '2rem', fontFamily: 'system-ui, sans-serif' }}>
      <h1>Library</h1>
      <ul>
        {books.map((b) => (
          <li key={b.id}>
            <strong>{b.title}</strong> — {b.author ?? 'Unknown author'} · {b.files} files ·{' '}
            {formatHours(b.durationSeconds)}
          </li>
        ))}
      </ul>
    </main>
  )
}
