﻿using IoTSharp.Data;
using IoTSharp.Dtos;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace IoTSharp.Extensions
{
    public static class AlarmExtension
    {

        internal static async Task<ApiResult<Alarm>> OccurredAlarm(this ApplicationDbContext _context, CreateAlarmDto cad)
        {
            Guid OriginatorId = Guid.Empty;
            OriginatorType originatorType = cad.OriginatorType;
            if (cad.OriginatorType == OriginatorType.Device || cad.OriginatorType == OriginatorType.Gateway || cad.OriginatorType == OriginatorType.Unknow)
            {
                var dev = _context.Device.Include(d=>d.Tenant).Include(d=>d.Customer).FirstOrDefault(d => d.Id.ToString() == cad.OriginatorName || d.Name == cad.OriginatorName);
                if (dev != null)
                {
                    if (dev.DeviceType == DeviceType.Gateway)
                    {
                        if (dev.Id.ToString() != cad.OriginatorName && dev.Name != cad.OriginatorName)
                        {
                            var subdev = from g in _context.Device.Include(d => d.Tenant).Include(d => d.Customer).Include(g => g.Owner) where g.Owner == dev && g.Name == cad.OriginatorName select g;
                            var orig = await subdev.FirstOrDefaultAsync();
                            OriginatorId = orig.Id;
                            originatorType = OriginatorType.Device;
                        }
                        else
                        {
                            originatorType = OriginatorType.Gateway;
                            OriginatorId = dev.Id;
                        }
                    }
                    else if (dev.DeviceType == DeviceType.Device)
                    {
                        originatorType = OriginatorType.Device;
                        OriginatorId = dev.Id;
                    }
                    return await _context.OccurredAlarm(cad, _alarm =>
                    {
                        _alarm.OriginatorType = originatorType;
                        _alarm.OriginatorId = OriginatorId;
                        _alarm.Tenant = dev.Tenant;
                        _alarm.Customer = dev.Customer;
                    });
                }
                else return new ApiResult<Alarm>(ApiCode.NotFoundDevice, "Originator name  not a device!",null);
            }
            else if (cad.OriginatorType == OriginatorType.Asset)
            {
                var ass = _context.Assets.Include(a => a.Tenant).Include(a => a.Customer).FirstOrDefault(d => d.Id.ToString() == cad.OriginatorName || d.Name == cad.OriginatorName);
                if (ass != null)
                {
                    originatorType = OriginatorType.Asset;
                    OriginatorId = ass.Id;
                    return await _context.OccurredAlarm(cad, _alarm =>
                    {
                        _alarm.OriginatorType = originatorType;
                        _alarm.OriginatorId = OriginatorId;
                        _alarm.Tenant = ass.Tenant;
                        _alarm.Customer = ass.Customer;
                    });
                }
                else return new ApiResult<Alarm>(ApiCode.NotFoundDevice, "Originator name not a asset",null);
            }
            else return new ApiResult<Alarm>(ApiCode.NotFoundDevice, "Originator name not a asset",null);
        }

      

        internal static async Task<ApiResult<Alarm>> OccurredAlarm(this ApplicationDbContext _context, CreateAlarmDto dto, Action<Alarm> action)
        {
            var result = new ApiResult<Alarm>(ApiCode.InValidData,"",null);
            try
            {
                var alarm = new Alarm
                {
                    Id = Guid.NewGuid(),
                    AckDateTime = DateTime.Now,
                    AlarmDetail = dto.AlarmDetail,
                    AlarmStatus = AlarmStatus.Active_UnAck,
                    AlarmType = dto.AlarmType,
                    ClearDateTime = new DateTime(1970, 1, 1),
                    EndDateTime = new DateTime(1970, 1, 1),
                    Propagate = true,
                    Serverity = dto.Serverity,
                    StartDateTime = DateTime.Now,
                };
                action?.Invoke(alarm);
                var isone = from a in _context.Alarms where a.OriginatorId == alarm.OriginatorId && a.AlarmType == alarm.AlarmType && a.Serverity == alarm.Serverity select a;
                if (isone.Any())
                {
                    var old = isone.First();
                    old.AlarmDetail = alarm.AlarmDetail;
                    old.EndDateTime = DateTime.Now;
                }
                else
                {

                    _context.Alarms.Add(alarm);
                }
                int ret = await _context.SaveChangesAsync();
                result = new ApiResult<Alarm>(ret > 0 ? ApiCode.Success : ApiCode.NothingToDo, ret > 0 ? "OK" : "No data", alarm);
            }
            catch (Exception ex)
            {
                result = new ApiResult<Alarm>(ApiCode.Exception, ex.Message,null);
            }
            return result;
        }
    }
}