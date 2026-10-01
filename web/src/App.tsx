import { BrowserRouter, Link, Route, Routes } from 'react-router'
import { NowPlayingBar } from './components/NowPlayingBar'
import { BookPage } from './pages/BookPage'
import { LibraryPage } from './pages/LibraryPage'
import { PlayerProvider } from './player/PlayerProvider'

export default function App() {
  return (
    <BrowserRouter>
      {/* Above the routes, so the active book keeps playing across pages. */}
      <PlayerProvider>
        <header className="masthead frame">
          <Link to="/" className="wordmark">
            <span className="ribbon-mark" aria-hidden="true" />
            Audiobooks
          </Link>
        </header>
        <main className="app frame">
          <Routes>
            <Route path="/" element={<LibraryPage />} />
            <Route path="/books/:id" element={<BookPage />} />
          </Routes>
        </main>
        <NowPlayingBar />
      </PlayerProvider>
    </BrowserRouter>
  )
}
