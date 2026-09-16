import { Component, type ErrorInfo, type ReactNode } from 'react'

interface Props {
  children: ReactNode
}

interface State {
  error: Error | null
}

/**
 * Last line of defence for a render-time crash. Without it a single bad render blanks the whole
 * app, which in a timed exam looks exactly like data loss; with it the exam is still on screen
 * behind a recoverable message, and reloading resumes the session from the server.
 */
export class ErrorBoundary extends Component<Props, State> {
  state: State = { error: null }

  static getDerivedStateFromError(error: Error): State {
    return { error }
  }

  componentDidCatch(error: Error, info: ErrorInfo): void {
    console.error('Unhandled render error', error, info.componentStack)
  }

  private reset = (): void => {
    this.setState({ error: null })
  }

  render(): ReactNode {
    const { error } = this.state
    if (!error) return this.props.children

    return (
      <div className="stack" style={{ maxWidth: '42rem', margin: '3rem auto', padding: '0 1rem' }}>
        <div className="card">
          <h1>Something broke in the page</h1>
          <p className="muted">
            Your answers are saved on the server as you go, so reloading picks the session back up
            where it was.
          </p>
          <p className="muted small">{error.message}</p>
          <div className="button-row">
            <button type="button" className="primary" onClick={() => window.location.reload()}>
              Reload the app
            </button>
            <button type="button" className="ghost" onClick={this.reset}>
              Try this page again
            </button>
          </div>
        </div>
      </div>
    )
  }
}
