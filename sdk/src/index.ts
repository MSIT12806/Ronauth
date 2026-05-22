import type {
  AccessTokenStore,
  AuthenticationResponse,
  PasswordLoginInput,
  RegisterUserInput,
  UserProfile,
  ValidationErrorBag,
} from './types'

export type {
  AccessTokenStore,
  AuthenticationResponse,
  PasswordLoginInput,
  RegisterUserInput,
  UserProfile,
  ValidationErrorBag,
} from './types'

export class RonAuthRequestError extends Error {
  status: number

  constructor(message: string, status: number) {
    super(message)
    this.name = 'RonAuthRequestError'
    this.status = status
  }
}

export class RonAuthValidationError extends RonAuthRequestError {
  errors: ValidationErrorBag

  constructor(errors: ValidationErrorBag) {
    super('validation failed', 400)
    this.name = 'RonAuthValidationError'
    this.errors = errors
  }
}

export interface RonAuthClientOptions {
  baseUrl?: string
  fetchImplementation?: typeof fetch
  tokenStore?: AccessTokenStore
}

export interface RonAuthClient {
  register(input: RegisterUserInput): Promise<AuthenticationResponse>
  login(input: PasswordLoginInput): Promise<AuthenticationResponse>
  bootstrap(): Promise<AuthenticationResponse | null>
  refresh(): Promise<AuthenticationResponse | null>
  logout(): Promise<void>
  getCurrentUser(): Promise<UserProfile | null>
  getAccessToken(): string
}

export function createMemoryAccessTokenStore(): AccessTokenStore {
  let accessToken = ''

  return {
    get() {
      return accessToken
    },
    set(token: string) {
      accessToken = token
    },
    clear() {
      accessToken = ''
    },
  }
}

export function createRonAuthClient(options: RonAuthClientOptions = {}): RonAuthClient {
  const fetchImplementation = options.fetchImplementation ?? fetch
  const tokenStore = options.tokenStore ?? createMemoryAccessTokenStore()
  const baseUrl = (options.baseUrl ?? '/api/auth').replace(/\/$/, '')

  return {
    async register(input) {
      const response = await sendRequest<AuthenticationResponse>(`${baseUrl}/register`, {
        method: 'POST',
        body: JSON.stringify(input),
      })
      syncAccessToken(tokenStore, response)
      return response
    },
    async login(input) {
      const response = await sendRequest<AuthenticationResponse>(`${baseUrl}/login`, {
        method: 'POST',
        body: JSON.stringify(input),
      })
      syncAccessToken(tokenStore, response)
      return response
    },
    async bootstrap() {
      const response = await sendNullableAuthenticationRequest(`${baseUrl}/bootstrap`, { method: 'GET' })
      syncAccessToken(tokenStore, response)
      return response
    },
    async refresh() {
      const response = await sendNullableAuthenticationRequest(`${baseUrl}/refresh`, { method: 'POST' })
      syncAccessToken(tokenStore, response)
      return response
    },
    async logout() {
      try {
        await sendRequest<void>(`${baseUrl}/logout`, { method: 'POST' })
      } finally {
        tokenStore.clear()
      }
    },
    async getCurrentUser() {
      const response = await fetchImplementation(`${baseUrl}/me`, createInit({ method: 'GET' }, tokenStore.get()))
      if (response.status === 401) {
        tokenStore.clear()
        return null
      }

      if (!response.ok) {
        throw new RonAuthRequestError(`request failed with status ${response.status}`, response.status)
      }

      return (await response.json()) as UserProfile
    },
    getAccessToken() {
      return tokenStore.get()
    },
  }

  async function sendNullableAuthenticationRequest(url: string, init: RequestInit) {
    const response = await fetchImplementation(url, createInit(init, tokenStore.get()))
    if (response.status === 401) {
      tokenStore.clear()
      return null
    }

    return parseAuthenticationResponse(response)
  }

  async function sendRequest<T>(url: string, init: RequestInit) {
    const response = await fetchImplementation(url, createInit(init, tokenStore.get()))
    if (response.ok) {
      if (response.status === 204) {
        return undefined as T
      }

      return (await response.json()) as T
    }

    const contentType = response.headers.get('Content-Type') ?? ''
    if (contentType.includes('application/json')) {
      const payload = await response.json() as Record<string, unknown>
      if (isAuthenticationResponse(payload)) {
        return payload as T
      }

      if (response.status === 400 && isValidationErrorPayload(payload)) {
        throw new RonAuthValidationError(payload.errors)
      }
    }

    throw new RonAuthRequestError(`request failed with status ${response.status}`, response.status)
  }

  async function parseAuthenticationResponse(response: Response) {
    if (!response.ok) {
      throw new RonAuthRequestError(`request failed with status ${response.status}`, response.status)
    }

    const payload = await response.json() as Record<string, unknown>
    if (!isAuthenticationResponse(payload)) {
      throw new RonAuthRequestError('unexpected authentication response payload', response.status)
    }

    return payload
  }
}

function createInit(init: RequestInit, accessToken: string): RequestInit {
  return {
    ...init,
    credentials: 'include',
    headers: {
      'Content-Type': 'application/json',
      ...(accessToken ? { Authorization: `Bearer ${accessToken}` } : {}),
      ...(init.headers ?? {}),
    },
  }
}

function isValidationErrorPayload(payload: Record<string, unknown>): payload is { errors: ValidationErrorBag } {
  return typeof payload === 'object' && payload !== null && typeof payload.errors === 'object' && payload.errors !== null
}

function isAuthenticationResponse(payload: Record<string, unknown>): payload is Record<string, unknown> & AuthenticationResponse {
  return typeof payload.status === 'string'
    && typeof payload.message === 'string'
    && 'accessToken' in payload
    && 'user' in payload
}

function syncAccessToken(tokenStore: AccessTokenStore, response: AuthenticationResponse | null) {
  if (!response) {
    tokenStore.clear()
    return
  }

  if (response.accessToken) {
    tokenStore.set(response.accessToken)
  }
}