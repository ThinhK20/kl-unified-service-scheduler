import { useQuery } from '@tanstack/react-query';
import { getAssignmentPreview } from '../../api/appointmentsApi';
import { queryKeys } from '../../lib/queryKeys';

export interface AssignmentPreviewParams {
  dealershipId: number | null;
  serviceTypeId: number | null;
  /** The chosen start, ISO-8601 with the dealership offset; '' = nothing chosen. */
  startTime: string;
  vehicleId: number | null;
  /** Only ask while the chosen time is offered as free. */
  enabled: boolean;
}

/** Who would serve the booking if it were made now (technician + bay, and the state of everyone else). */
export function useAssignmentPreview({ dealershipId, serviceTypeId, startTime, vehicleId, enabled }: AssignmentPreviewParams) {
  return useQuery({
    queryKey: queryKeys.assignmentPreview(dealershipId ?? 0, serviceTypeId ?? 0, startTime, vehicleId),
    queryFn: ({ signal }) =>
      getAssignmentPreview(
        { dealershipId: dealershipId as number, serviceTypeId: serviceTypeId as number, startTime, vehicleId },
        signal,
      ),
    enabled: enabled && dealershipId !== null && serviceTypeId !== null && startTime !== '',
    // Like the free times: it changes as people book.
    staleTime: 0,
  });
}
