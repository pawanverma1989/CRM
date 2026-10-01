export type UserRole = 'admin' | 'manager' | 'sales_rep';
export type UserStatus = 'invited' | 'active' | 'deactivated';

export interface UserDto {
  id: string;
  organizationId: string;
  teamId?: string;
  email: string;
  firstName: string;
  lastName?: string;
  phone?: string;
  role: UserRole;
  status: UserStatus;
  lastLoginAt?: string;
  createdAt: string;
  updatedAt: string;
}

export interface SessionDto {
  id: string;
  ipAddress?: string;
  userAgent?: string;
  createdAt: string;
  expiresAt: string;
  lastUsedAt?: string;
}

export interface TeamDto {
  id: string;
  organizationId: string;
  name: string;
  managerId?: string;
  createdAt: string;
  updatedAt: string;
}

export interface OrganizationDto {
  id: string;
  name: string;
  defaultCurrency: string;
  timezone: string;
  createdAt: string;
  updatedAt: string;
}

export interface LoginResponse {
  accessToken: string;
  refreshToken: string;
  expiresIn: number;
  tokenType: string;
  user: UserDto;
}

export interface PaginatedResponse<T> {
  data: T[];
  nextCursor?: string;
  hasMore: boolean;
}

export interface ApiError {
  message: string;
  errors?: Record<string, string[]>;
}
