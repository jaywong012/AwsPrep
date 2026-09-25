import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import App from './App.tsx'
import { ErrorBoundary } from './components/ErrorBoundary'
import { RouterProvider } from './router'
import { AuthProvider } from './auth'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <ErrorBoundary>
      {/* Inside the router, because signing out and the post-sign-in redirect both navigate.
          Outside App, so the guard can swap the whole app for a sign-in form without taking the
          provider down with it. */}
      <RouterProvider>
        <AuthProvider>
          <App />
        </AuthProvider>
      </RouterProvider>
    </ErrorBoundary>
  </StrictMode>,
)
