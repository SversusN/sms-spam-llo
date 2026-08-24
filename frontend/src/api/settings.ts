import { apiClient } from './client';

export interface FeaturesResponse {
  requireMailingConsent: boolean;
}

export const getFeatures = async (): Promise<FeaturesResponse> => {
  const response = await apiClient.get<FeaturesResponse>('/settings/features');
  return response.data;
};
