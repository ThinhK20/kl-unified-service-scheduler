using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Scheduler.Application.Allocation;
using Scheduler.Application.Appointments.Book;
using Scheduler.Application.Appointments.Cancel;
using Scheduler.Application.Appointments.Queries;
using Scheduler.Application.Catalog.Queries;

namespace Scheduler.Application;

/// <summary>Registers the Application layer. Infrastructure must register the repositories, IUnitOfWork and IClock.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddValidatorsFromAssemblyContaining<BookAppointmentValidator>(ServiceLifetime.Scoped);

        services.AddSingleton<IResourceAllocationStrategy, ScarcityAwareStrategy>();
        services.AddScoped<AvailabilityService>();

        services.AddScoped<BookAppointmentHandler>();
        services.AddScoped<CancelAppointmentHandler>();
        services.AddScoped<GetAvailabilityHandler>();
        services.AddScoped<GetAvailabilityDaysHandler>();
        services.AddScoped<GetAssignmentPreviewHandler>();
        services.AddScoped<GetAppointmentsHandler>();
        services.AddScoped<GetAppointmentByIdHandler>();
        services.AddScoped<GetDealershipsHandler>();
        services.AddScoped<GetServiceTypesHandler>();
        services.AddScoped<GetCustomerVehiclesHandler>();
        services.AddScoped<GetCustomersHandler>();

        return services;
    }
}
