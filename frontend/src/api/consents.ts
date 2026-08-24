import { apiClient } from './client';

export interface ConsentDto {
  id: number;
  userGuid: string;
  patientSnils: string;
  patientName: string;
  birthDate: string | null;
  phone: string | null;
  isConsentGiven: boolean;
  consentType: string | null;
  createdAt: string;
  revokedAt: string | null;
}

export interface ConsentFilterRequest {
  patientSnils?: string;
  patientName?: string;
  isConsentGiven?: boolean;
  dateFrom?: string;
  dateTo?: string;
  page?: number;
  pageSize?: number;
}

export interface CreateConsentRequest {
  patientSnils: string;
  patientName: string;
  birthDate?: string | null;
  phone?: string | null;
  isConsentGiven: boolean;
  consentType?: string | null;
}

export interface PatientLookupResult {
  patientSnils: string;
  patientName: string;
  birthDate: string | null;
  phone: string | null;
}

export interface PagedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export const getConsents = async (filter: ConsentFilterRequest): Promise<PagedResult<ConsentDto>> => {
  const response = await apiClient.post<PagedResult<ConsentDto>>('/consents/list', filter);
  return response.data;
};

export const createConsent = async (data: CreateConsentRequest): Promise<{ id: number }> => {
  const response = await apiClient.post<{ id: number }>('/consents', data);
  return response.data;
};

export const lookupPatient = async (snils: string): Promise<PatientLookupResult> => {
  const response = await apiClient.get<PatientLookupResult>('/consents/patients/lookup', {
    params: { snils: snils.replace(/\D/g, '') },
  });
  return response.data;
};

export const revokeConsent = async (id: number): Promise<{ message: string }> => {
  const response = await apiClient.post<{ message: string }>(`/consents/${id}/revoke`);
  return response.data;
};

export const getConsentPdfUrl = (id: number): string => {
  return `${apiClient.defaults.baseURL}/consents/${id}/pdf`;
};
