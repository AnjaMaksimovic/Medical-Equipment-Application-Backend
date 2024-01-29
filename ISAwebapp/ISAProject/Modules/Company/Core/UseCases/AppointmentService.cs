﻿using AutoMapper;
using FluentResults;
using ISAProject.Configuration.Core.UseCases;
using ISAProject.Modules.Company.API.Dtos;
using ISAProject.Modules.Company.API.Public;
using ISAProject.Modules.Company.Core.Domain;
using ISAProject.Modules.Company.Core.Domain.RepositoryInterfaces;
using ISAProject.Modules.Database;
using ISAProject.Modules.Stakeholders.Core.Domain;
using ISAProject.Modules.Stakeholders.Core.Domain.RepositoryInterfaces;
using Microsoft.IdentityModel.Tokens;
namespace ISAProject.Modules.Company.Core.UseCases
{
    public class AppointmentService: MappingService<AppointmentDto, Appointment>, IAppointmentService
    {
        private readonly IAppointmentRepository _repository;
        private readonly DatabaseContext _dbContext;

        private readonly ICompanyAdminRepo _companyAdminRepo;
        public AppointmentService(IMapper mapper, IAppointmentRepository repository, ICompanyAdminRepo companyRepo, DatabaseContext dbContext) : base(mapper)
        {
            _repository = repository;
            _companyAdminRepo = companyRepo;
            _dbContext = dbContext;
        }
        public Result<AppointmentDto> Create(AppointmentDto appointmentDto)
        {
            var appointment = MapToDomain(appointmentDto);
            using var transaction = _dbContext.Database.BeginTransaction();
            try
            {
                var isTimeSlotAvailable = _repository.IsTimeSlotAvailable(appointment.Start, appointment.Duration, appointment.CompanyId, _dbContext);
                if (!isTimeSlotAvailable)
                {
                    transaction.Rollback();
                    return Result.Fail(FailureCode.NotFound).WithError("Error! Not available time slot for appointment");
                }

                _dbContext.Appointments.Add(appointment);
                _dbContext.SaveChanges();
                transaction.Commit();
                return MapToDto(appointment);
            }
            catch (Exception e)
            {
                transaction.Rollback();
                return Result.Fail(FailureCode.Internal).WithError($"An error occurred: {e.Message}");
            }
        }

        public Result<AppointmentDto> ReserveScheduledAppointment(AppointmentDto appointmentDto)
        {
            var equipmentDtos = appointmentDto.Equipment;
            var equipmentIds = equipmentDtos.Select(e => e.Id).ToList();

            var existingEquipment = _repository.GetWithIds(equipmentIds);
            var appointment = MapToDomain(appointmentDto);
            appointment.Equipment = existingEquipment;
            foreach (var eq in appointment.Equipment)
            {
                eq.ReservedQuantity += 1;
            }
            return MapToDto(_repository.Create(appointment));
        }

        public Result<AppointmentDto> Get(int id)
        {
            var encounter = _repository.Get(id);
            return MapToDto(encounter);

        }
        public Result<AppointmentDto> Update(AppointmentDto appointmentDto)
        {
            try
            {
                var result = _repository.Update(MapToDomain(appointmentDto));
                return MapToDto(result);
            }
            catch (KeyNotFoundException e)
            {
                return Result.Fail(FailureCode.NotFound).WithError(e.Message);
            }
            catch (ArgumentException e)
            {
                return Result.Fail(FailureCode.InvalidArgument).WithError(e.Message);
            }

        }

        public Result<AppointmentDto> ReserveAppointment(AppointmentDto appointmentDto)
        {
            var equipmentDtos = appointmentDto.Equipment;
            var equipmentIds = equipmentDtos.Select(e => e.Id).ToList();
            using (var transaction = _dbContext.Database.BeginTransaction())
            {
                try
                {
                    var appointment = _repository.GetById(appointmentDto.Id, _dbContext);
                    if (appointment == null)
                    {
                        transaction.Rollback();
                        return Result.Fail(FailureCode.Conflict).WithError("Error! Appointment not found or version mismatch");
                    }
                    if (!appointment.IsAvailableForReservation())
                    {
                        transaction.Rollback();
                        return Result.Fail(FailureCode.Forbidden).WithError("Error! Appointment is already reserved");
                    }
                    appointment.CustomerId = appointmentDto.CustomerId;
                    appointment.CustomerName = appointmentDto.CustomerName;
                    appointment.CustomerSurname = appointmentDto.CustomerSurname;

                    var existingEquipment = _repository.GetWithIds(equipmentIds);
                    appointment.Equipment = existingEquipment;
                    foreach (var equipment in appointment.Equipment)
                    {
                        _dbContext.Entry(equipment).Reload();
                        if (equipment.Quantity <= 0)
                        {
                            transaction.Rollback();
                            return Result.Fail(FailureCode.InsufficientData);
                        }
                        equipment.ReservedQuantity++;
                    }
                    _dbContext.Update(appointment);
                    _dbContext.SaveChanges();
                    transaction.Commit();
                    return MapToDto(appointment);
                }
                catch (Exception e)
                {
                    transaction.Rollback();
                    return Result.Fail(FailureCode.Internal).WithError($"An error occurred: {e.Message}");
                }
            }
        }

        public Result<List<AppointmentDto>> GetAll()
        {
            var appointments = _repository.GetAll();
            return MapToDto(appointments);
        }
        
        private Company.Core.Domain.Company GetAppointmentsCompany(int id)
        {
            var company = _repository.GetAppointmentsCompany(id);
            return company;
        }

        private List<Appointment> GenerateAppointmentsForAllDay(DateTime selectedDate, Core.Domain.Company company, List<CompanyAdmin> administrators)
        {
            DateTime currentTime = selectedDate.Date.Add(company.WorkingHours.OpeningHours);
            DateTime endTime = selectedDate.Date.Add(company.WorkingHours.ClosingHours);

            var recommendedAppointments = new List<Appointment>();

            while (currentTime.AddMinutes(60) <= endTime)
            {
                User availableAdmin = administrators.FirstOrDefault();
                if (availableAdmin != null)
                {
                    var recommendedAppointment = new Appointment
                    {
                        Start = currentTime,
                        AdminName = availableAdmin.Name,
                        AdminSurname = availableAdmin.Surname,
                        CustomerName = "",
                        CustomerSurname = "",
                        CompanyId = company.Id,
                    };

                    recommendedAppointments.Add(recommendedAppointment);
                }

                currentTime = currentTime.AddMinutes(60);
            }
            return recommendedAppointments;
        }

        public Result<List<Appointment>> GenerateRecommendedAppointments(DateTime selectedDate, int companyId)
        {
            var allAppointments = GetAll().Value;

            var company = GetAppointmentsCompany(companyId);

            var recommendedAppointments = new List<Appointment>();

            var openingHours = company.WorkingHours.OpeningHours;
            var closingHours = company.WorkingHours.ClosingHours;

            var administrators = _companyAdminRepo.GetCompanyAdmins(companyId);

            var existingAppointments = allAppointments
                .Where(a => a.Start.Date == selectedDate.Date &&
                            a.Start.TimeOfDay >= openingHours &&
                            a.Start.TimeOfDay < closingHours)
                .ToList();

            if (existingAppointments.IsNullOrEmpty())
            {
                recommendedAppointments = GenerateAppointmentsForAllDay(selectedDate, company, administrators);
            }
            else
            {
                DateTime currentTime = selectedDate.Date.Add(openingHours);
                DateTime endTime = selectedDate.Date.Add(closingHours);

                while (currentTime.AddMinutes(existingAppointments.First().Duration) <= endTime)
                {
                    User availableAdmin = FindAvailableAdministrator(administrators, existingAppointments, currentTime);
                    if (availableAdmin != null)
                    {
                        var recommendedAppointment = new Appointment
                        {
                            Start = currentTime,
                            AdminName = availableAdmin.Name,
                            AdminSurname = availableAdmin.Surname,
                            CustomerName = "",
                            CustomerSurname = "",
                            CompanyId = companyId,
                        };

                        recommendedAppointments.Add(recommendedAppointment);
                    }

                    currentTime = currentTime.AddMinutes(existingAppointments.First().Duration);
                }
            }

            return recommendedAppointments;
        }

        private User FindAvailableAdministrator(List <CompanyAdmin> administrators, List<AppointmentDto> existingAppointments, DateTime currentTime)
        {
            foreach (var admin in administrators)
            {
                bool hasAppointment = false;

                foreach(var appointment in existingAppointments)
                {
                    if (appointment.Start.Ticks <= currentTime.Ticks && 
                        appointment.Start.AddMinutes(appointment.Duration) > currentTime &&
                        appointment.AdminName == admin.Name &&
                        appointment.AdminSurname == admin.Surname)
                    {
                        hasAppointment = true;
                        break;
                    }
                }

                if (!hasAppointment)
                {
                    return admin;
                }

            }
            return null;
        }

        public bool IsAppointmentValid (DateTime selectedDate, int companyId, string adminName, string adminSurname)
        {
            var allAppointments = GetAll().Value;
            bool isAppointmentValid = !allAppointments.Any(appointment =>
                 appointment.CompanyId == companyId &&
                 appointment.AdminName == adminName &&
                 appointment.AdminSurname == adminSurname &&
                 appointment.Start <= selectedDate &&
                 selectedDate < appointment.Start.AddMinutes(appointment.Duration));

            return isAppointmentValid;

        }

        public Result<List<AppointmentDto>> GetCompanyAppointments(int companyId)
        {
            var appointments = _repository.GetCompanyAppointments(companyId);
            return MapToDto(appointments);
        }

        public Result<List<AppointmentDto>> GetCustomerAppointments(int customerId)
        {
            var appointments = _repository.GetCustomerAppointments(customerId);
            return MapToDto(appointments);
        }

        public bool IsEquipmentReserved(int equipmentId)
        {
            var allAppointments = GetAll().Value;
            bool canEquipmentBeDeleted = !allAppointments.Any(appointment =>
                appointment.Equipment.Any(equipment =>
                    equipment.Id == equipmentId
                )
            );

            return canEquipmentBeDeleted;

        }

        public List<string> RetrieveBarcodeImageData(string userId)
        {
            string barcodeFolderPath = "BarCodes";
            string[] barcodeFilePaths = Directory.GetFiles(barcodeFolderPath, $"{userId}_*.png");
            List<byte[]> imageDataList = new List<byte[]>();

            foreach (string barcodeFilePath in barcodeFilePaths)
            {
                byte[] imageData = File.ReadAllBytes(barcodeFilePath);
                imageDataList.Add(imageData);
            }
            byte[][] imageDataArray = imageDataList.ToArray();
            List<string> base64ImageStrings = new List<string>();
            
            foreach (byte[] imageData in imageDataArray)
            {
                string base64ImageString = Convert.ToBase64String(imageData);
                base64ImageStrings.Add(base64ImageString);
            }
            return base64ImageStrings;
        }
    }
}