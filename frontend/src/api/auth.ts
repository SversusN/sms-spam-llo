import { apiClient } from './client';

export interface LoginRequest {
  login: string;
  password: string;
}

export interface LoginResponse {
  token: string;
  userName: string;
  userGuid: string;
}

export interface UserListItem {
  guid: string;
  code: string;
  name: string;
}

export const login = async (data: LoginRequest): Promise<LoginResponse> => {
  const response = await apiClient.post<LoginResponse>('/auth/login', data);
  return response.data;
};

export const getUsers = async (): Promise<UserListItem[]> => {
  const response = await apiClient.get<UserListItem[]>('/auth/users');
  return response.data;
};
