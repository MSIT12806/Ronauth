export type AuthenticationStatus = 'Success' | 'Failed' | 'RequiresSecondFactor' | 'LockedOut'

export interface UserProfile {
  userId: string
  userName: string
  email: string
  enabledTwoFactorProviders: string[]
}

export interface UserAccess {
  roleId: string
  roleName: string
  scopeId: string
  scopeName: string
}

export interface AuthenticationResponse {
  status: AuthenticationStatus
  failureCode: string
  message: string
  accessToken: string
  temporaryToken: string
  secondFactorProvider: string
  user: UserProfile | null
  accesses: UserAccess[]
}

export interface RegisterUserInput {
  userName: string
  email: string
  password: string
}

export interface PasswordLoginInput {
  userName: string
  password: string
}

export interface ValidationErrorBag {
  [key: string]: string[]
}

export interface AccessTokenStore {
  get(): string
  set(token: string): void
  clear(): void
}