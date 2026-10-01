import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
// One family for everything: EB Garamond, upright and italic.
import '@fontsource-variable/eb-garamond'
import '@fontsource-variable/eb-garamond/wght-italic.css'
import './index.css'
import App from './App.tsx'
import { checkSession } from './auth/auth'

// Track whether the last input was a pointer or the keyboard. Chrome shows a
// focus ring on sliders and selects even after a mouse click; CSS uses this to
// keep those rings for keyboard users only. Buttons and links don't need it,
// since :focus-visible already gets them right.
const setInput = (mode: 'pointer' | 'keyboard') => () => {
  document.documentElement.dataset.input = mode
}
document.addEventListener('pointerdown', setInput('pointer'), { capture: true, passive: true })
document.addEventListener('keydown', setInput('keyboard'), { capture: true, passive: true })

// Before the first render, so the app starts in 'checking' rather than flashing
// the login page at a signed-in user.
void checkSession()

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
)
