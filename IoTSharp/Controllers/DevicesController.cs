﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Dynamic.Core;
using System.Linq.Expressions;
using System.Threading.Tasks;
using IoTSharp.Controllers.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using IoTSharp.Data;
using Microsoft.AspNetCore.Authorization;
using IoTSharp.Dtos;
using Dic = System.Collections.Generic.Dictionary<string, string>;
using DicKV = System.Collections.Generic.KeyValuePair<string, string>;
using MQTTnet.Client;
using MQTTnet.Extensions.Rpc;
using MQTTnet.Protocol;
using IoTSharp.Extensions;
using IoTSharp.Models;
using MQTTnet.Exceptions;
using MQTTnet.Client.Options;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using IoTSharp.Storage;
using k8s.Models;
using Newtonsoft.Json.Linq;
using MQTTnet.AspNetCoreEx;
using MQTTnet.Server.Status;

namespace IoTSharp.Controllers
{
    /// <summary>
    /// 设备管理
    /// </summary>
    [Route("api/[controller]")]
    [Authorize]
    [ApiController]
    public class DevicesController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IMqttClientOptions _mqtt;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly SignInManager<IdentityUser> _signInManager;
        private readonly ILogger _logger;
        private readonly IStorage _storage;
        private readonly IMqttServerEx _serverEx;

        public DevicesController(UserManager<IdentityUser> userManager,
            SignInManager<IdentityUser> signInManager, ILogger<DevicesController> logger, IMqttServerEx serverEx, ApplicationDbContext context, IMqttClientOptions mqtt, IStorage storage)
        {
            _context = context;
            _mqtt = mqtt;
            _userManager = userManager;
            _signInManager = signInManager;
            _logger = logger;
            _storage = storage;
            _serverEx = serverEx;
        }
        /// <summary>
        /// 获取指定客户的设备列表
        /// </summary>
        /// <param name="customerId"></param>
        /// <returns></returns>
        // GET: api/Customers/All
        [HttpGet("Devices/Customers/All")]
        [Authorize(Roles = nameof(UserRole.NormalUser))]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResult<Guid>), StatusCodes.Status404NotFound)]
        [ProducesDefaultResponseType]
        public async Task<ApiResult<List<Device>>> GetAllDevices([FromQuery] Guid customerId)
        {
            var f = from c in _context.Device where c.Customer.Id == customerId select c;
            if (!f.Any())
            {
                return new ApiResult<List<Device>>(ApiCode.CustomerDoesNotHaveDevice, $"The customer {customerId} does not have any device", null);
            }
            else
            {
                return new ApiResult<List<Device>>(ApiCode.Success, $"The customer {customerId} does not have any device", await f.ToListAsync());
            }

        }
        /// <summary>
        /// 获取指定客户的设备列表
        /// </summary>
        /// <returns></returns>
        // GET: api/Devices/Customers
        [HttpGet("Customers")]
        [Authorize(Roles = nameof(UserRole.NormalUser))]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResult<Guid>), StatusCodes.Status404NotFound)]
        [ProducesDefaultResponseType]
        public async Task<ApiResult<PagedData<DeviceDetailDto>>> GetDevices([FromQuery] DeviceParam m)
        {

            Expression<Func<Device, bool>> condition = x => x.Customer.Id == m.customerId;
            return new ApiResult<PagedData<DeviceDetailDto>>(ApiCode.Success, "OK", new PagedData<DeviceDetailDto>
            {
                total = await _context.Device.CountAsync(condition),
                rows = await _context.Device.OrderByDescending(c => c.LastActive).Where(condition).Skip((m.offset) * m.limit).Take(m.limit).Join(_context.DeviceIdentities, x => x.Id, y => y.Device.Id, (x, y) => new DeviceDetailDto()
                {
                    Id = x.Id,
                    Name = x.Name,
                    LastActive = x.LastActive,
                    IdentityId = y.IdentityId,
                    IdentityValue = y.IdentityValue,
                    Tenant = x.Tenant,
                    Customer = x.Customer,
                    DeviceType = x.DeviceType,
                    Online = x.Online,
                    Owner = x.Owner,
                    Timeout = x.Timeout


                }).ToListAsync()
            });



        }

        /// <summary>
        /// 获取指定设备的认证方式信息
        /// </summary>
        /// <param name="deviceId"></param>
        /// <returns></returns>
        [Authorize(Roles = nameof(UserRole.NormalUser))]
        [HttpGet("{deviceId}/Identity")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResult), StatusCodes.Status404NotFound)]
        [ProducesDefaultResponseType]
        public async Task<ApiResult<DeviceIdentity>> GetIdentity(Guid deviceId)
        {

            var did = await _context.DeviceIdentities.FirstOrDefaultAsync(c => c.Device.Id == deviceId);
            if (did == null)
            {
                return new ApiResult<DeviceIdentity>(ApiCode.CantFindObject, "CantFindObject", null);
            }
            else
            {
                return new ApiResult<DeviceIdentity>(ApiCode.Success, "OK", did);
            }

        }

        /// <summary>
        ///获取指定设备的最新属性
        /// </summary>
        /// <param name="deviceId"></param>
        /// <returns></returns>
        [Authorize(Roles = nameof(UserRole.NormalUser))]
        [HttpGet("{deviceId}/AttributeLatest")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResult), StatusCodes.Status404NotFound)]
        [ProducesDefaultResponseType]
        public async Task<ApiResult<List<AttributeDataDto>>> GetAttributeLatest(Guid deviceId)
        {
            Device dev = Found(deviceId);
            if (dev == null)
            {
                return new ApiResult<List<AttributeDataDto>>(ApiCode.CantFindObject, "Device's Identity not found", null);
            }
            else
            {
                var devid = from t in _context.AttributeLatest
                            where t.DeviceId == deviceId
                            select new AttributeDataDto()
                            {
                                DataSide = t.DataSide,
                                DateTime = t.DateTime,
                                KeyName = t.KeyName,
                                DataType = t.Type,
                                Value = t.ToObject()
                            };
                if (!devid.Any())
                {
                    return new ApiResult<List<AttributeDataDto>>(ApiCode.CantFindObject, "Device's Identity not found", null);
                }
                return new ApiResult<List<AttributeDataDto>>(ApiCode.Success, "Ok", await devid.ToListAsync());
            }
        }
        /// <summary>
        /// 获取指定设备指定keys的最新属性
        /// </summary>
        /// <param name="deviceId">Which device do you read?</param>
        /// <param name="keys">Specify key name list , use , or space or  ; to split </param>
        /// <returns></returns>
        [Authorize(Roles = nameof(UserRole.NormalUser))]
        [HttpGet("{deviceId}/AttributeLatest/{keys}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResult), StatusCodes.Status404NotFound)]
        [ProducesDefaultResponseType]
        public async Task<ApiResult<List<AttributeDataDto>>> GetAttributeLatest(Guid deviceId, string keys)
        {
            Device dev = Found(deviceId);
            if (dev == null)
            {

                return new ApiResult<List<AttributeDataDto>>(ApiCode.NotFoundDevice, "Device's  not found", null);
            }
            else
            {
                var kv = from t in _context.AttributeLatest where t.DeviceId == t.DeviceId && keys.Split(',', ' ', ';').Contains(t.KeyName) select new AttributeDataDto() { DataSide = t.DataSide, DateTime = t.DateTime, KeyName = t.KeyName, DataType = t.Type, Value = t.ToObject() };

                return new ApiResult<List<AttributeDataDto>>(ApiCode.Success, "Ok", await kv.ToListAsync());


            }
        }

        private Device Found(Guid deviceId)
        {
            return FoundAsync(deviceId).GetAwaiter().GetResult();
        }
        private async Task<Device> FoundAsync(Guid deviceId)
        {
            Device dev = null;
            if (User.IsInRole(nameof(UserRole.TenantAdmin)))
            {
                var tid = User.Claims.First(c => c.Type == IoTSharpClaimTypes.Tenant);
                dev = await _context.Device.Include(d => d.Tenant).FirstOrDefaultAsync(d => d.Id == deviceId && d.Tenant.Id.ToString() == tid.Value);
            }
            else if (User.IsInRole(nameof(UserRole.NormalUser)))
            {
                var cid = User.Claims.First(c => c.Type == IoTSharpClaimTypes.Customer);
                dev = await _context.Device.Include(d => d.Customer).FirstOrDefaultAsync(d => d.Id == deviceId && d.Customer.Id.ToString() == cid.Value);
            }
            return dev;
        }

        /// <summary>
        ///获取指定设备的最新遥测数据
        /// </summary>
        /// <param name="deviceId"></param>
        /// <returns></returns>
        [Authorize(Roles = nameof(UserRole.NormalUser))]
        [HttpGet("{deviceId}/TelemetryLatest")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResult), StatusCodes.Status404NotFound)]
        [ProducesDefaultResponseType]
        public async Task<ApiResult<List<TelemetryDataDto>>> GetTelemetryLatest(Guid deviceId)
        {

        

            Random r = new Random((int) System.DateTime.Now.Ticks);
            var l = new List<TelemetryDataDto>();
            for (int i = 0; i < 10; i++)
            {
                TelemetryDataDto t= new TelemetryDataDto();
                t.Value = r.Next(1,10);
                t.KeyName = "tele"+i;
                t.DateTime= DateTime.Now;
                t.DataType = DataType.Double;
                l.Add(t);
            }

            return new ApiResult<List<TelemetryDataDto>>(ApiCode.Success, "Ok",
                l);


            Device dev = Found(deviceId);
            if (dev == null)
            {
                return new ApiResult<List<TelemetryDataDto>>(ApiCode.NotFoundDeviceIdentity, "Device's Identity not found", null);

            }

            try
            {
                return new ApiResult<List<TelemetryDataDto>>(ApiCode.Success, "Ok",
                    await _storage.GetTelemetryLatest(deviceId));
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error,ex.Message);
                return new ApiResult<List<TelemetryDataDto>>(ApiCode.Exception, ex.Message,
                    null);
            }



        }



 
        /// <summary>
        /// 获取指定设备的指定key 的遥测数据
        /// </summary>
        /// <param name="deviceId">Which device do you read?</param>
        /// <param name="keys">指定键值列表， 使用分号或者逗号分割 。 </param>
        /// <returns></returns>
        [Authorize(Roles = nameof(UserRole.NormalUser))]
        [HttpGet("{deviceId}/TelemetryLatest/{keys}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResult), StatusCodes.Status404NotFound)]
        [ProducesDefaultResponseType]
        public async Task<ApiResult<List<TelemetryDataDto>>> GetTelemetryLatest(Guid deviceId, string keys)
        {
            Device dev = Found(deviceId);
            if (dev == null)
            {
                return new ApiResult<List<TelemetryDataDto>>(ApiCode.NotFoundDeviceIdentity, "Device's Identity not found", null);

            }
            else
            {
                return new ApiResult<List<TelemetryDataDto>>(ApiCode.Success, "Ok", await _storage.GetTelemetryLatest(deviceId, keys));

            }
        }


        /// <summary>
        /// 获取指定设备和指定时间， 指定key的数据
        /// </summary>
        /// <param name="deviceId">Which device do you read?</param>
        /// <param name="keys">Specify key name list , use , or space or  ; to split </param>
        /// <param name="begin">开始以时间， 比如 2019-06-06 12:24</param>
        /// <returns></returns>
        [Authorize(Roles = nameof(UserRole.NormalUser))]
        [HttpGet("{deviceId}/TelemetryLatest/{keyName}/{begin}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResult), StatusCodes.Status404NotFound)]
        [ProducesDefaultResponseType]
        public async Task<ApiResult<List<TelemetryDataDto>>> GetTelemetryData(Guid deviceId, string keys, DateTime begin)
        {
            Device dev = Found(deviceId);
            if (dev == null)
            {
                return new ApiResult<List<TelemetryDataDto>>(ApiCode.NotFoundDeviceIdentity, "Device's Identity not found", null);

            }
            else
            {

                return new ApiResult<List<TelemetryDataDto>>(ApiCode.Success, "Ok",
                    keys == "all"
                        ? await _storage.LoadTelemetryAsync(deviceId, begin)
                        : await _storage.LoadTelemetryAsync(deviceId, keys, begin));



            }
        }
        /// <summary>
        /// 返回指定设备的的遥测数据， 按照keyname 和指定时间范围获取，如果keyname 为 all  , 则返回全部key 的数据
        /// </summary>
        /// <param name="deviceId">Which device do you read?</param>
        /// <param name="keys">Specify key name list , use , or space or  ; to split </param>
        /// <param name="begin">For example: 2019-06-06 12:24</param>
        /// <param name="end">For example: 2019-06-06 12:24</param>
        /// <returns></returns>
        [Authorize(Roles = nameof(UserRole.NormalUser))]
        [HttpGet("{deviceId}/TelemetryData/{keyName}/{begin}/{end}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResult), StatusCodes.Status404NotFound)]
        [ProducesDefaultResponseType]
        public async Task<ApiResult<List<TelemetryDataDto>>> GetTelemetryData(Guid deviceId, string keys, DateTime begin, DateTime end)
        {
            Device dev = Found(deviceId);
            if (dev == null)
            {
                return new ApiResult<List<TelemetryDataDto>>(ApiCode.NotFoundDeviceIdentity, "Device's Identity not found", null);

            }
            else
            {
                return new ApiResult<List<TelemetryDataDto>>(ApiCode.Success, "Ok",
                    keys == "all" ? await _storage.LoadTelemetryAsync(deviceId, begin, end) : await _storage.LoadTelemetryAsync(deviceId, keys, begin, end));

            }
        }




        /// <summary>
        /// 获取设备详情
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        // GET: api/Devices/5
        [Authorize(Roles = nameof(UserRole.NormalUser))]
        [HttpGet("{id}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResult<Guid>), StatusCodes.Status404NotFound)]
        [ProducesDefaultResponseType]
        public async Task<ApiResult<Device>> GetDevice(Guid id)
        {
            Device device = await FoundAsync(id);
            if (device == null)
            {
                return new ApiResult<Device>(ApiCode.NotFoundDeviceIdentity, "Device's Identity not found", null);

            }
            return new ApiResult<Device>(ApiCode.Success, "Ok", device);

        }

        /// <summary>
        /// 修改设备
        /// </summary>
        /// <param name="id"></param>
        /// <param name="device"></param>
        /// <returns></returns>
        // PUT: api/Devices/5
        [Authorize(Roles = nameof(UserRole.CustomerAdmin))]
        [HttpPut("{id}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResult<Guid>), StatusCodes.Status404NotFound)]
        [ProducesDefaultResponseType]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<ApiResult<bool>> PutDevice(Guid id, DevicePutDto device)
        {
            if (id != device.Id)
            {
                return new ApiResult<bool>(ApiCode.InValidData, "Device's Identity not InValidData", false);
            }

            var cid = User.Claims.First(c => c.Type == IoTSharpClaimTypes.Customer);
            var tid = User.Claims.First(c => c.Type == IoTSharpClaimTypes.Tenant);
            var dev = _context.Device.Include(d => d.Tenant).Include(d => d.Customer).First(d => d.Id == device.Id);
            var tenid = dev.Tenant.Id;
            var cusid = dev.Customer.Id;

            if (dev == null)
            {
                return new ApiResult<bool>(ApiCode.NotFoundDeviceIdentity, "Device's Identity not found", false);
            }
            else if (dev.Tenant?.Id.ToString() != tid.Value || dev.Customer?.Id.ToString() != cid.Value)
            {


                return new ApiResult<bool>(ApiCode.DoNotAllow, "Do not allow access to devices from other customers or tenants", false);
                // return BadRequest(new ApiResult(ApiCode.DoNotAllow, $"Do not allow access to devices from other customers or tenants"));
            }
            dev.Name = device.Name;
            try
            {
                await _context.SaveChangesAsync();

                return new ApiResult<bool>(ApiCode.Success, "Ok", true);
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!DeviceExists(id))
                {
                    //  return NotFound(new ApiResult<Guid>(ApiCode.NotFoundDevice, $"Device {id} not found ", id));

                    return new ApiResult<bool>(ApiCode.NotFoundDevice, "Device {id} not found ", false);
                }
                else
                {
                    throw;
                }
            }


        }

        /// <summary>
        /// 创建设备， 客户ID和租户ID均为当前登录用户所属
        /// </summary>
        /// <param name="device"></param>
        /// <returns></returns>
        // POST: api/Devices
        [Authorize(Roles = nameof(UserRole.CustomerAdmin))]
        [HttpPost]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResult<DevicePostDto>), StatusCodes.Status404NotFound)]
        [ProducesDefaultResponseType]
        public async Task<ApiResult<Device>> PostDevice(DevicePostDto device)
        {
            var cid = User.Claims.First(c => c.Type == IoTSharpClaimTypes.Customer);
            var tid = User.Claims.First(c => c.Type == IoTSharpClaimTypes.Tenant);
            var devvalue = new Device() { Name = device.Name, DeviceType = device.DeviceType, Timeout = 300, LastActive = DateTime.Now };
            devvalue.Tenant = _context.Tenant.Find(new Guid(tid.Value));
            devvalue.Customer = _context.Customer.Find(new Guid(cid.Value));
            if (devvalue.Tenant == null || devvalue.Customer == null)
            {


                return new ApiResult<Device>(ApiCode.NotFoundTenantOrCustomer, "Not found Tenant or Customer", null);

            }
            _context.Device.Add(devvalue);
            _context.AfterCreateDevice(devvalue);
            await _context.SaveChangesAsync();
            return new ApiResult<Device>(ApiCode.Success, "Ok", await FoundAsync(devvalue.Id));
        }

        /// <summary>
        /// 删除设备
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        [Authorize(Roles = nameof(UserRole.CustomerAdmin))]
        [HttpDelete("{id}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResult<Guid>), StatusCodes.Status404NotFound)]
        [ProducesDefaultResponseType]
        public async Task<ApiResult<Device>> DeleteDevice(Guid id)
        {
            Device device = Found(id);
            if (device == null)
            {


                return new ApiResult<Device>(ApiCode.NotFoundTenantOrCustomer, "Device {id} not found", null);
            }
            _context.Device.Remove(device);
            await _context.SaveChangesAsync();
            return new ApiResult<Device>(ApiCode.Success, "Ok", device);
        }

        private bool DeviceExists(Guid id)
        {
            return _context.Device.Any(e => e.Id == id);
        }

        /// <summary>
        /// 远程控制指定设备， 此方法通过给远程设备发送mqtt消息进行控制，设备在收到信息后回复结果，此方法才算调用结束
        /// </summary>
        /// <param name="access_token"></param>
        /// <param name="method"></param>
        /// <param name="timeout"></param>
        /// <param name="args"></param>
        /// <returns></returns>
        [AllowAnonymous]
        [HttpPost("{access_token}/Rpc/{method}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResult<Dic>), StatusCodes.Status404NotFound)]
        [ProducesDefaultResponseType]
        public async Task<ActionResult<string>> Rpc(string access_token, string method, int timeout, object args)
        {
            ActionResult<string> result = null;
            var (ok, dev) = _context.GetDeviceByToken(access_token);
            if (ok)
            {
                return Ok(new ApiResult<Dic>(ApiCode.NotFoundDevice, $"{access_token} not a device's access token", new Dic(new DicKV[] { new DicKV("access_token", access_token) })));
            }
            else
            {
                try
                {
                    var rpcClient = new RpcClient(_mqtt);
                    var _timeout = TimeSpan.FromSeconds(timeout);
                    var qos = MqttQualityOfServiceLevel.AtMostOnce;
                    var payload = Newtonsoft.Json.JsonConvert.SerializeObject(args);
                    await rpcClient.ConnectAsync();
                    var response = await rpcClient.ExecuteAsync(_timeout, dev.Id.ToString(), method, payload, qos);
                    await rpcClient.DisconnectAsync();
                    result = Ok(System.Text.Encoding.UTF8.GetString(response));
                }
                catch (MqttCommunicationTimedOutException ex1)
                {
                    result = Ok(new ApiResult(ApiCode.RPCTimeout, $"{dev.Id} RPC Timeout {ex1.Message}"));
                }
                catch (Exception ex)
                {
                    result = Ok(new ApiResult(ApiCode.RPCFailed, $"{dev.Id} RPCFailed {ex.Message}"));
                }
            }
            return result;
        }

        /// <summary>
        /// HTTP方式上传遥测数据
        /// </summary>
        /// <param name="access_token">Device 's access token</param>
        /// <param name="telemetrys"></param>
        /// <returns></returns>
        [AllowAnonymous]
        [HttpPost("{access_token}/Telemetry")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResult<Dic>), StatusCodes.Status404NotFound)]
        [ProducesDefaultResponseType]
        public async Task<ActionResult<ApiResult<Dic>>> Telemetry(string access_token, Dictionary<string, object> telemetrys)
        {
            Dic exceptions = new Dic();
            var (ok, device) = _context.GetDeviceByToken(access_token);
            if (ok)
            {
                return Ok(new ApiResult<Dic>(ApiCode.NotFoundDevice, $"{access_token} not a device's access token", new Dic(new DicKV[] { new DicKV("access_token", access_token) })));
            }
            else
            {
                var result = await _context.SaveAsync<TelemetryLatest>(telemetrys, device.Id, DataSide.ClientSide);
                return Ok(new ApiResult<Dic>(result.ret > 0 ? ApiCode.Success : ApiCode.NothingToDo, result.ret > 0 ? "OK" : "No Telemetry save", new Dic(result.exceptions?.Select(f => new DicKV(f.Key, f.Value.Message)))));
            }
        }

        /// <summary>
        /// 获取服务测的设备熟悉
        /// </summary>
        /// <param name="access_token">Device 's access token </param>
        ///<param name="dataSide">Specifying data side.</param>
        ///<param name="keys">Specifying Attribute's keys</param>
        /// <returns></returns>
        [AllowAnonymous]
        [HttpGet("{access_token}/Attributes/{dataSide}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResult), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResult<Dic>), StatusCodes.Status404NotFound)]
        [ProducesDefaultResponseType]
        public async Task<ActionResult<AttributeLatest>> Attributes(string access_token, DataSide dataSide, string keys)
        {
            Dic exceptions = new Dic();
            var (ok, device) = _context.GetDeviceByToken(access_token);
            if (ok)
            {
                return Ok(new ApiResult<Dic>(ApiCode.NotFoundDevice, $"{access_token} not a device's access token", new Dic(new DicKV[] { new DicKV("access_token", access_token) })));
            }
            else
            {
                var deviceId = device.Id;
                try
                {
                    var attributes = from dev in _context.AttributeLatest where dev.DeviceId == deviceId select dev;
                    var fs = from at in await attributes.ToListAsync() where at.DataSide == dataSide && keys.Split(',', options: StringSplitOptions.RemoveEmptyEntries).Contains(at.KeyName) select at;
                    return Ok(new ApiResult<AttributeLatest[]>(ApiCode.Success, "Ok", fs.ToArray()));
                }
                catch (Exception ex)
                {
                    return Ok(new ApiResult(ApiCode.Exception, $"{deviceId}  {ex.Message}"));
                }
            }
        }

        /// <summary>
        /// 上传客户侧属性数据
        /// </summary>
        /// <param name="access_token">Device 's access token </param>
        /// <param name="attributes">attributes</param>
        /// <returns></returns>
        [AllowAnonymous]
        [HttpPost("{access_token}/Attributes")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResult<Dic>), StatusCodes.Status404NotFound)]
        [ProducesDefaultResponseType]
        public async Task<ActionResult<ApiResult<Dic>>> Attributes(string access_token, Dictionary<string, object> attributes)
        {
            Dic exceptions = new Dic();
            var (ok, dev) = _context.GetDeviceByToken(access_token);
            if (ok)
            {
                return Ok(new ApiResult<Dic>(ApiCode.NotFoundDevice, $"{access_token} not a device's access token", new Dic(new DicKV[] { new DicKV("access_token", access_token) })));
            }
            else
            {
                var result = await _context.SaveAsync<AttributeLatest>(attributes, dev.Id, DataSide.ClientSide);
                return Ok(new ApiResult<Dic>(result.ret > 0 ? ApiCode.Success : ApiCode.NothingToDo, result.ret > 0 ? "OK" : "No Attribute save", new Dic(result.exceptions?.Select(f => new DicKV(f.Key, f.Value.Message)))));
            }
        }

        /// <summary>
        /// SessionStatus
        /// </summary>
        /// <param name="deviceId"></param>
        /// <returns></returns>
        [Authorize(Roles = nameof(UserRole.NormalUser))]
        [HttpGet("SessionStatus")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesDefaultResponseType]
        public async Task<ApiResult<IList<IMqttSessionStatus>>> GetSessionStatus(Guid deviceId)
        {
            return new ApiResult<IList<IMqttSessionStatus>>(ApiCode.Success, "OK", await _serverEx.GetSessionStatusAsync());
        }
        /// <summary>
        /// SessionStatus
        /// </summary>
        /// <param name="deviceId"></param>
        /// <returns></returns>
        [Authorize(Roles = nameof(UserRole.NormalUser))]
        [HttpGet("ClientStatus")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesDefaultResponseType]
        public async Task<ApiResult<IList<IMqttClientStatus>>> GetClientStatus(Guid deviceId)
        {
            return new ApiResult<IList<IMqttClientStatus>>(ApiCode.Success, "OK", await _serverEx.GetClientStatusAsync());
        }
    }
}