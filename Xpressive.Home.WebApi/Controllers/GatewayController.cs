using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using System.Web.Http;
using Xpressive.Home.Contracts.Automation;
using Xpressive.Home.Contracts.Gateway;
using Xpressive.Home.Contracts.Messaging;
using Xpressive.Home.Contracts.Rooms;
using Xpressive.Home.Contracts.Variables;

namespace Xpressive.Home.WebApi.Controllers
{
    [RoutePrefix("api/v1/gateway")]
    public class GatewayController : ApiController
    {
        private readonly IMessageQueue _messageQueue;
        private readonly IDictionary<string, IGateway> _gateways;

        public GatewayController(IMessageQueue messageQueue, IEnumerable<IGateway> gateways)
        {
            _messageQueue = messageQueue;
            _gateways = gateways.ToDictionary(g => g.Name);
        }

        [HttpGet, Route("")]
        public IEnumerable<string> GetGatewayNames()
        {
            return _gateways.Keys;
        }

        [HttpGet, Route("{gatewayName}")]
        public IEnumerable<IDevice> GetDevices(string gatewayName)
        {
            IGateway gateway;
            if (_gateways.TryGetValue(gatewayName, out gateway))
            {
                return gateway.Devices;
            }

            return null;
        }

        [HttpGet, Route("{gatewayName}/empty")]
        public Dictionary<string, object> CreateEmptyDevice(string gatewayName)
        {
            var result = new Dictionary<string, object>();

            IGateway gateway;
            if (_gateways.TryGetValue(gatewayName, out gateway) && gateway.CanCreateDevices)
            {
                var device = gateway.CreateEmptyDevice();
                var properties = GetDeviceProperties(device);

                foreach (var property in properties)
                {
                    var type = property.PropertyType;
                    var value = type.IsValueType ? Activator.CreateInstance(type) : null;
                    result.Add(property.Name, value);
                }
            }

            return result;
        }

        [HttpPost, Route("{gatewayName}")]
        public IHttpActionResult CreateDevice(string gatewayName, [FromBody]Dictionary<string, object> dto)
        {
            IGateway gateway;
            if (_gateways.TryGetValue(gatewayName, out gateway) && gateway.CanCreateDevices)
            {
                var device = gateway.CreateEmptyDevice();
                var properties = GetDeviceProperties(device);

                foreach (var property in properties)
                {
                    object value;
                    if (dto.TryGetValue(property.Name, out value))
                    {
                        var converted = Convert.ChangeType(value, property.PropertyType);
                        property.SetValue(device, converted);
                    }
                }

                var success = gateway.AddDevice(device);

                if (success)
                {
                    return Ok();
                }
            }

            return InternalServerError();
        }

        [HttpPut, Route("{gatewayName}/{deviceId}/{actionName}")]
        public IHttpActionResult ExecuteAction(string gatewayName, string deviceId, string actionName, [FromBody]Dictionary<string, string> parameters)
        {
            IGateway gateway;
            if (!_gateways.TryGetValue(gatewayName, out gateway) ||
                !gateway.Actions.Any(a => a.Name.Equals(actionName, StringComparison.Ordinal)))
            {
                return NotFound();
            }

            var device = gateway.Devices.SingleOrDefault(d => d.Id.Equals(deviceId, StringComparison.Ordinal));

            if (device == null)
            {
                return NotFound();
            }

            _messageQueue.Publish(new CommandMessage(gatewayName, deviceId, actionName, parameters));
            return StatusCode(HttpStatusCode.NoContent);
        }

        [HttpGet, Route("{gatewayName}/actions")]
        public IEnumerable<ActionDto> GetActions(string gatewayName)
        {
            IGateway gateway;
            if (!_gateways.TryGetValue(gatewayName, out gateway))
            {
                return Enumerable.Empty<ActionDto>();
            }

            return gateway.Actions.Select(a => new ActionDto
            {
                Name = a.Name,
                Fields = a.Fields.ToArray()
            });
        }

        private IList<PropertyInfo> GetDeviceProperties(IDevice device)
        {
            var result = new List<PropertyInfo>();

            if (device != null)
            {
                var pairs = new List<Tuple<int, PropertyInfo>>();
                var properties = device.GetType().GetProperties();

                foreach (var property in properties)
                {
                    var attribute = property.GetCustomAttribute<DevicePropertyAttribute>(inherit: true);
                    if (attribute != null)
                    {
                        pairs.Add(Tuple.Create(attribute.SortOrder, property));
                    }
                }

                result.AddRange(pairs.OrderBy(p => p.Item1).Select(p => p.Item2));
            }

            return result;
        }

        public class ActionDto
        {
            public string Name { get; set; }
            public string[] Fields { get; set; }
        }
    }

    [RoutePrefix("api/v1/variable")]
    public class VariableController : ApiController
    {
        private readonly IVariableRepository _variableRepository;
        private readonly IDictionary<string, IGateway> _gateways;

        public VariableController(IVariableRepository variableRepository, IEnumerable<IGateway> gateways)
        {
            _variableRepository = variableRepository;
            _gateways = gateways.ToDictionary(g => g.Name);
        }

        [HttpGet, Route("{gatewayName}/{deviceId}")]
        public IEnumerable<VariableDto> Get(string gatewayName, string deviceId)
        {
            IGateway gateway;
            if (!_gateways.TryGetValue(gatewayName, out gateway) ||
                !gateway.Devices.Any(d => d.Id.Equals(deviceId, StringComparison.Ordinal)))
            {
                return Enumerable.Empty<VariableDto>();
            }

            var prefix = $"{gatewayName}.{deviceId}.";
            var variables = _variableRepository.Get().Where(v => v.Name.StartsWith(prefix, StringComparison.Ordinal));

            return variables.Select(v => new VariableDto
            {
                Name = v.Name.Substring(0, prefix.Length),
                Value = v.Value
            });
        }

        public class VariableDto
        {
            public string Name { get; set; }
            public object Value { get; set; }
        }
    }

    [RoutePrefix("api/v1/script")]
    public class ScriptController : ApiController
    {
        private readonly IScriptRepository _repository;
        private readonly IScriptEngine _engine;

        public ScriptController(IScriptRepository repository, IScriptEngine engine)
        {
            _repository = repository;
            _engine = engine;
        }

        [HttpGet, Route("")]
        public async Task<IEnumerable<NameIdDto>> GetScripts()
        {
            var scripts = await _repository.GetAsync();
            return scripts.Select(s => new NameIdDto { Id = s.Id, Name = s.Name });
        }

        [HttpDelete, Route("{scriptId}")]
        public async Task Delete(string scriptId)
        {
            await _repository.DeleteAsync(scriptId);
        }

        public class NameIdDto
        {
            public string Id { get; set; }
            public string Name { get; set; }
        }
    }

    [RoutePrefix("api/v1/schedule")]
    public class ScriptSchedulerController : ApiController
    {
        private readonly ICronService _cronService;

        public ScriptSchedulerController(ICronService cronService)
        {
            _cronService = cronService;
        }

        [HttpGet, Route("")]
        public async Task<IEnumerable<ScheduledScript>> Get()
        {
            return await _cronService.GetSchedulesAsync();
        }

        [HttpPost, Route("{scriptId}")]
        public async Task Schedule(string scriptId, [FromBody]string cronTab)
        {
            await _cronService.ScheduleAsync(scriptId, cronTab);
        }

        [HttpDelete, Route("{scheduleId}")]
        public async Task DeleteSchedule(string scheduleId)
        {
            await _cronService.DeleteScheduleAsync(scheduleId);
        }
    }

    [RoutePrefix("api/v1/room")]
    public class RoomController : ApiController
    {
        private readonly IRoomRepository _repository;

        public RoomController(IRoomRepository repository)
        {
            _repository = repository;
        }

        [HttpGet, Route("")]
        public async Task<IEnumerable<Room>> Get()
        {
            return await _repository.GetAsync();
        }

        [HttpPost, Route("")]
        public async Task<Room> Create([FromBody] Room room)
        {
            room = new Room
            {
                Name = room.Name,
                Icon = string.Empty
            };

            await _repository.SaveAsync(room);
            return room;
        }

        [HttpPut, Route("")]
        public async Task Update([FromBody] Room room)
        {
            if (room.Id == Guid.Empty)
            {
                throw new ArgumentException("Id must not be empty", nameof(room));
            }

            await _repository.SaveAsync(room);
        }

        [HttpDelete, Route("")]
        public async Task Delete([FromBody] Room room)
        {
            if (room.Id == Guid.Empty)
            {
                throw new ArgumentException("Id must not be empty", nameof(room));
            }

            await _repository.DeleteAsync(room);
        }
    }
}