using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Web.Http;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Agencias;
using NestoAPI.Infraestructure.Agencias.Innovatrans;
using NestoAPI.Infraestructure.Agencias.Tarifas;
using NestoAPI.Models;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// Una agencia de transporte (fila de AgenciasTransporte). OJO: Numero es la PK de cada fila
    /// (agencia × empresa), no del transportista; el comparador usa Numero como AgenciaId.
    /// RecargoCombustible es fracción (0,1055 = 10,55 %).
    /// </summary>
    public class AgenciaTransporteDTO
    {
        public int Numero { get; set; }
        public string Empresa { get; set; }
        public string Nombre { get; set; }
        public string Ruta { get; set; }
        public string Identificador { get; set; }
        public string PrefijoCodigoBarras { get; set; }
        public string CuentaReembolsos { get; set; }
        public decimal RecargoCombustible { get; set; }
    }

    /// <summary>
    /// Mantenimiento de agencias de transporte server-side (Nesto#340): alta/edición de agencias
    /// (incluido el recargo de combustible, editable mensual) y comparador "agencia más económica"
    /// para un pedido. La cuarentena se gestiona en el cliente vía el parámetro AgenciasEnCuarentena.
    /// No hay borrado: las agencias tienen movimientos (FK), no se pueden eliminar.
    /// </summary>
    public class AgenciasTarifasController : ApiController
    {
        public AgenciasTarifasController()
        {
            db = new NVEntities();
            db.Configuration.LazyLoadingEnabled = false;
        }

        public AgenciasTarifasController(NVEntities db)
        {
            this.db = db;
            db.Configuration.LazyLoadingEnabled = false;
        }

        private readonly NVEntities db;

        // GET: api/Agencias  -> todas las agencias con todos sus campos (mantenimiento).
        [HttpGet]
        [Route("api/Agencias")]
        public IHttpActionResult GetAgencias()
        {
            var agencias = db.AgenciasTransportes
                .OrderBy(a => a.Numero)
                .ThenBy(a => a.Empresa)
                .ToList()
                .Select(ADto)
                .ToList();

            return Ok(agencias);
        }

        // POST: api/Agencias  -> alta de una agencia nueva (p.ej. Innovatrans, Numero=12).
        [HttpPost]
        [Route("api/Agencias")]
        public IHttpActionResult PostAgencia([FromBody] AgenciaTransporteDTO dto)
        {
            IHttpActionResult error = Validar(dto);
            if (error != null)
            {
                return error;
            }
            if (db.AgenciasTransportes.Any(a => a.Numero == dto.Numero))
            {
                return BadRequest($"Ya existe una agencia con el número {dto.Numero}.");
            }

            var agencia = new AgenciaTransporte
            {
                Numero = dto.Numero,
                Empresa = dto.Empresa?.Trim(),
                Nombre = dto.Nombre?.Trim(),
                Ruta = dto.Ruta?.Trim(),
                Identificador = dto.Identificador?.Trim(),
                PrefijoCodigoBarras = dto.PrefijoCodigoBarras?.Trim(),
                CuentaReembolsos = dto.CuentaReembolsos?.Trim(),
                RecargoCombustible = dto.RecargoCombustible,
                Usuario = UsuarioAuditoriaHelper.Resolver(User, "NestoAPI"),
                FechaModificacion = DateTime.Now
            };
            db.AgenciasTransportes.Add(agencia);
            db.SaveChanges();

            return Ok(ADto(agencia));
        }

        // PUT: api/Agencias/{numero}  -> edición de una agencia existente (todos los campos).
        [HttpPut]
        [Route("api/Agencias/{numero:int}")]
        public IHttpActionResult PutAgencia(int numero, [FromBody] AgenciaTransporteDTO dto)
        {
            IHttpActionResult error = Validar(dto);
            if (error != null)
            {
                return error;
            }

            var agencia = db.AgenciasTransportes.FirstOrDefault(a => a.Numero == numero);
            if (agencia == null)
            {
                return NotFound();
            }

            agencia.Empresa = dto.Empresa?.Trim();
            agencia.Nombre = dto.Nombre?.Trim();
            agencia.Ruta = dto.Ruta?.Trim();
            agencia.Identificador = dto.Identificador?.Trim();
            agencia.PrefijoCodigoBarras = dto.PrefijoCodigoBarras?.Trim();
            agencia.CuentaReembolsos = dto.CuentaReembolsos?.Trim();
            agencia.RecargoCombustible = dto.RecargoCombustible;
            agencia.Usuario = UsuarioAuditoriaHelper.Resolver(User, "NestoAPI");
            agencia.FechaModificacion = DateTime.Now;
            db.SaveChanges();

            return Ok(ADto(agencia));
        }

        // GET: api/Agencias/MasEconomica?empresa=&codigoPostal=&peso=&reembolso=&pais=
        // pais en ISO 3166-1 alpha-2 (ES, PT, FR...); "ES" por defecto (retrocompatible). El destino
        // canónico es (codigoPostal + pais): cada tarifa resuelve su zona puertas adentro.
        [HttpGet]
        [Route("api/Agencias/MasEconomica")]
        public IHttpActionResult GetMasEconomica(string codigoPostal, decimal peso, string empresa = "1", decimal reembolso = 0, string pais = "ES")
        {
            // Solo se comparan agencias dadas de alta en AgenciasTransporte: una tarifa portada pero
            // sin fila (p.ej. Innovatrans antes de crearla) no debe entrar en la comparación.
            var numerosExistentes = db.AgenciasTransportes.Select(a => a.Numero).Distinct().ToList();
            // Las agencias sombra compiten en el ranking interno pero NUNCA se auto-seleccionan.
            var idsSombra = db.AgenciasTransportes.Where(a => a.EsSombra).Select(a => a.Numero).ToList();
            var registro = new RegistroTarifasExistentes(new RegistroTarifas(), numerosExistentes);
            var comparador = new ComparadorAgencias(registro, new ProveedorRecargoCombustibleEF(db), idsSombra);
            OpcionEnvioAgencia mejor = comparador.MasEconomica(empresa, codigoPostal, peso, reembolso, pais);

            if (mejor == null)
            {
                return NotFound();
            }
            return Ok(mejor);
        }

        // GET: api/Agencias/{numero}/Coste?codigoPostal=&peso=&empresa=&reembolso=&servicioId=
        // Coste de UNA agencia (y opcionalmente un servicio) para el destino, con su fuel. A diferencia
        // de MasEconomica NO elige la más barata: devuelve el coste de la agencia indicada (la realmente
        // usada en el envío), para rellenar EnviosAgencia.ImporteGasto (NestoAPI#238). NotFound si esa
        // agencia/servicio no tiene tarifa portada o no cubre la zona.
        [HttpGet]
        [Route("api/Agencias/{numero:int}/Coste")]
        public IHttpActionResult GetCosteAgencia(int numero, string codigoPostal, decimal peso,
            string empresa = "1", decimal reembolso = 0, byte? servicioId = null, string pais = "ES")
        {
            var numerosExistentes = db.AgenciasTransportes.Select(a => a.Numero).Distinct().ToList();
            var idsSombra = db.AgenciasTransportes.Where(a => a.EsSombra).Select(a => a.Numero).ToList();
            var registro = new RegistroTarifasExistentes(new RegistroTarifas(), numerosExistentes);
            var comparador = new ComparadorAgencias(registro, new ProveedorRecargoCombustibleEF(db), idsSombra);
            OpcionEnvioAgencia opcion = comparador.CosteDeAgencia(empresa, codigoPostal, peso, reembolso, numero, servicioId, pais);

            if (opcion == null)
            {
                return NotFound();
            }
            return Ok(opcion);
        }

        // POST: api/Agencias/ComparativaSombra/Recalcular?dias=30
        // Rellena ComparativaAgenciaSombra con los envíos reales de los últimos N días (idempotente):
        // qué agencia SOMBRA habría ganado cada envío y a qué coste vs la agencia usada. Para backfill
        // histórico bajo demanda; el job nocturno 'comparativa-agencia-sombra' hace lo mismo a diario.
        [HttpPost]
        [Authorize]
        [Route("api/Agencias/ComparativaSombra/Recalcular")]
        public IHttpActionResult RecalcularComparativaSombra(int dias = 30)
        {
            int insertados = new ComparativaAgenciaSombraJobsService(db).RegistrarComparativas(dias);
            return Ok(new { dias, insertados });
        }

        // GET: api/Agencias/Innovatrans/Diagnostico?codigoPostal=28001
        // Prueba de conectividad con el WebService DataTrans DTX de Innovatrans. Usa BuscarPoblacion
        // (solo lectura: NO crea ni modifica envíos), por lo que es seguro lanzarlo contra producción.
        // Valida de paso las credenciales: 200/300 = conectado y autenticado, 400 = clave/usuario mal.
        [HttpGet]
        [Route("api/Agencias/Innovatrans/Diagnostico")]
        public async Task<IHttpActionResult> GetDiagnosticoInnovatrans(string codigoPostal = "28001")
        {
            try
            {
                string identificador = db.AgenciasTransportes
                    .Where(a => a.Numero == Constantes.Agencias.AGENCIA_INNOVATRANS)
                    .Select(a => a.Identificador)
                    .FirstOrDefault();
                var cliente = new ClienteSoapDataTrans(new ConfiguracionInnovatrans(identificador?.Trim()));
                var operaciones = new OperacionesLecturaDataTrans(cliente);
                ResultadoBuscarPoblacion resultado = await operaciones.BuscarPoblacionAsync(codigoPostal);

                string estado;
                switch (resultado.Respuesta)
                {
                    case 200: estado = "CONECTADO"; break;                    // auth ok + CP con población
                    case 300: estado = "CONECTADO_SIN_RESULTADOS"; break;     // auth ok, CP sin población
                    case 400: estado = "AUTENTICACION_INCORRECTA"; break;     // clave (MD5) o usuario mal
                    default: estado = "RESPUESTA_INESPERADA"; break;
                }

                return Ok(new
                {
                    estado,
                    respuesta = resultado.Respuesta,
                    mensajeError = resultado.MensajeError,
                    poblaciones = resultado.Poblaciones
                });
            }
            catch (DataTransException ex)
            {
                return Content(HttpStatusCode.BadGateway, new { estado = "ERROR_CONEXION", detalle = ex.Message });
            }
        }

        private IHttpActionResult Validar(AgenciaTransporteDTO dto)
        {
            if (dto == null)
            {
                return BadRequest("No se han recibido datos de la agencia.");
            }
            if (string.IsNullOrWhiteSpace(dto.Nombre))
            {
                return BadRequest("El nombre de la agencia es obligatorio.");
            }
            if (dto.RecargoCombustible < 0)
            {
                return BadRequest("El recargo de combustible no puede ser negativo.");
            }
            return null;
        }

        private static AgenciaTransporteDTO ADto(AgenciaTransporte a) => new AgenciaTransporteDTO
        {
            Numero = a.Numero,
            Empresa = a.Empresa?.Trim(),
            Nombre = a.Nombre?.Trim(),
            Ruta = a.Ruta?.Trim(),
            Identificador = a.Identificador?.Trim(),
            PrefijoCodigoBarras = a.PrefijoCodigoBarras?.Trim(),
            CuentaReembolsos = a.CuentaReembolsos?.Trim(),
            RecargoCombustible = a.RecargoCombustible
        };
    }
}
