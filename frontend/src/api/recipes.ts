import { apiClient } from './client';

export interface RecipeFilterRequest {
  dateFrom?: string;
  dateTo?: string;
  patientName?: string;
  patientPhone?: string;
  lsName?: string;
  individualSnils?: string;
  contractorGuid?: string;
  onlyDeferred?: boolean;
  onlyNotSent?: boolean;
  sortColumn?: string;
  sortDirection?: string;
  page?: number;
  pageSize?: number;
}

export interface RecipeDto {
  recipeId: number;
  apName: string;
  lsName: string;
  incomeDate: string | null;
  dateNumberRecipe: string | null;
  dateIssueEnd: string | null;
  expirationDate: string | null;
  patientName: string | null;
  patientPhone: string | null;
  saleDate: string | null;
  smsDate: string | null;
  individualSnils: string | null;
  program: string | null;
  dosage: string | null;
  quantity: string | null;
  smsStatus: string | null;
  smsDeliveryStatus: string | null;
  hasMailingConsent: boolean;
}

export interface PagedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export const getRecipes = async (filter: RecipeFilterRequest): Promise<PagedResult<RecipeDto>> => {
  const response = await apiClient.post<PagedResult<RecipeDto>>('/recipes/list', filter);
  return response.data;
};

export interface PharmacyDto {
  guid: string;
  name: string;
}

export const getPharmacies = async (): Promise<PharmacyDto[]> => {
  const response = await apiClient.get<PharmacyDto[]>('/recipes/pharmacies');
  return response.data;
};

export const exportRecipes = async (filter: RecipeFilterRequest): Promise<Blob> => {
  const response = await apiClient.post('/recipes/export', filter, {
    responseType: 'blob',
  });
  return response.data;
};
