import { BrowserRouter, Link, Route, Routes } from 'react-router'
import { BookPage } from './pages/BookPage'
import { LibraryPage } from './pages/LibraryPage'

export default function App() {
  return (
    <BrowserRouter>
      <header className="masthead">
        <Link to="/" className="wordmark">
          Audiobooks
        </Link>
      </header>
      <main className="app">
        <Routes>
          <Route path="/" element={<LibraryPage />} />
          <Route path="/books/:id" element={<BookPage />} />
        </Routes>
      </main>
    </BrowserRouter>
  )
}
