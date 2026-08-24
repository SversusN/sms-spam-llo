import { apiClient } from './client';

export interface QueueRecipesRequest {
  recipeIds: number[];
}

export const enqueueRecipes = async (data: QueueRecipesRequest): Promise<{ enqueued: number }> => {
  const response = await apiClient.post<{ enqueued: number }>('/smsqueue/enqueue', data);
  return response.data;
};

export interface SmsLog {
  id: number;
  recipeId: number;
  userGuid: string;
  individualSnils: string | null;
  phone: string;
  message: string;
  status: string;
  providerResponse: string | null;
  deliveryStatus: string | null;
  createdAt: string;
}

export interface SmsLogFilter {
  page?: number;
  pageSize?: number;
  status?: string;
  individualSnils?: string;
  recipeId?: number;
  dateFrom?: string;
  dateTo?: string;
}

export const getSmsLogs = async (filter: SmsLogFilter = {}): Promise<SmsLog[]> => {
  const response = await apiClient.get<SmsLog[]>('/smsqueue/logs', {
    params: filter,
  });
  return response.data;
};

export interface SmsQueueItem {
  id: number;
  recipeId: number;
  userGuid: string;
  individualSnils: string | null;
  createdAt: string;
  status: string;
  errorMessage: string | null;
  processedAt: string | null;
}

export interface SmsQueueFilter {
  page?: number;
  pageSize?: number;
  status?: string;
  individualSnils?: string;
  recipeId?: number;
  dateFrom?: string;
  dateTo?: string;
}

export const getSmsQueue = async (filter: SmsQueueFilter = {}): Promise<SmsQueueItem[]> => {
  const response = await apiClient.get<SmsQueueItem[]>('/smsqueue/queue', {
    params: filter,
  });
  return response.data;
};
