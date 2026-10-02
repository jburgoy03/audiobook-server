import { BrowserRouter, Link, Navigate, NavLink, Route, Routes } from 'react-router'
import { logout, useAuth } from './auth/auth'
import { LoginPage } from './auth/LoginPage'
import { PassphrasePage } from './auth/PassphrasePage'
import { NowPlayingBar } from './components/NowPlayingBar'
import { AdminPage } from './pages/AdminPage'
import { BookPage } from './pages/BookPage'
import { LibraryPage } from './pages/LibraryPage'
import { PlayerProvider } from './player/PlayerProvider'

export default function App() {
  const auth = useAuth()
  const signedIn = auth.status === 'signedIn'
  const isAdmin = auth.status === 'signedIn' && auth.isAdmin

  return (
    // Outside the auth gate, so the URL survives signing in: a reload of
    // /books/:id with an expired session comes back to that book.
    <BrowserRouter>
      <header className="masthead frame">
        <Link to="/" className="wordmark">
          <span className="ribbon-mark" aria-hidden="true" />
          Audiobooks
        </Link>
        {(signedIn || auth.status === 'mustChangePassword') && (
          <nav className="masthead-nav">
            {/* Convenience only: the server enforces admin on every admin route. */}
            {isAdmin && (
              <NavLink to="/admin" className="signout">
                Admin
              </NavLink>
            )}
            <button type="button" className="signout" onClick={() => void logout()}>
              Sign out
            </button>
          </nav>
        )}
      </header>

      {auth.status === 'checking' ? (
        <main className="app frame" />
      ) : auth.status === 'mustChangePassword' ? (
        // Like signed out, no player: the server would 403 the library anyway.
        <main className="app frame">
          <PassphrasePage username={auth.username} />
        </main>
      ) : signedIn ? (
        // Only while signed in. Signing out (or a 401) unmounts PlayerProvider,
        // which stops the active book, saves its position, and takes the
        // now-playing bar with it.
        <PlayerProvider>
          <main className="app frame">
            <Routes>
              <Route path="/" element={<LibraryPage />} />
              <Route path="/books/:id" element={<BookPage />} />
              {/* A non-admin who types the URL lands on the library. */}
              <Route path="/admin" element={isAdmin ? <AdminPage /> : <Navigate to="/" replace />} />
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
