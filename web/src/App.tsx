import { BrowserRouter, Link, Route, Routes } from 'react-router'
import { logout, useAuth } from './auth/auth'
import { LoginPage } from './auth/LoginPage'
import { NowPlayingBar } from './components/NowPlayingBar'
import { BookPage } from './pages/BookPage'
import { LibraryPage } from './pages/LibraryPage'
import { PlayerProvider } from './player/PlayerProvider'

export default function App() {
  const auth = useAuth()
  const signedIn = auth.status === 'signedIn'

  return (
    // Outside the auth gate, so the URL survives signing in: a reload of
    // /books/:id with an expired session comes back to that book.
    <BrowserRouter>
      <header className="masthead frame">
        <Link to="/" className="wordmark">
          <span className="ribbon-mark" aria-hidden="true" />
          Audiobooks
        </Link>
        {signedIn && (
          <button type="button" className="signout" onClick={() => void logout()}>
            Sign out
          </button>
        )}
      </header>

      {auth.status === 'checking' ? (
        <main className="app frame" />
      ) : signedIn ? (
        // Only while signed in. Signing out (or a 401) unmounts PlayerProvider,
        // which stops the active book, saves its position, and takes the
        // now-playing bar with it.
        <PlayerProvider>
          <main className="app frame">
            <Routes>
              <Route path="/" element={<LibraryPage />} />
              <Route path="/books/:id" element={<BookPage />} />
            </Routes>
          </main>
          <NowPlayingBar />
        </PlayerProvider>
      ) : (
        <main className="app frame">
          <LoginPage auth={auth} />
        </main>
      )}
    </BrowserRouter>
  )
}
