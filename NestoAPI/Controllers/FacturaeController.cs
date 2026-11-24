using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Facturas;
using NestoAPI.Infraestructure.Facturas.Facturae;
using NestoAPI.Infraestructure.Seguridad;
using NestoAPI.Infrastructure;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    /// <summary>NestoAPI#903: los DIR3 del organismo de un cliente (GET y cuerpo del PUT).</summary>
    public class ClienteFacturaElectronicaDTO
    {
        public string Empresa { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string OficinaContable { get; set; }
        public string OrganoGestor { get; set; }
        public string UnidadTramitadora { get; set; }
        public string OrganoProponente { get; set; }
        public string NombreOficinaContable { get; set; }
        public string NombreOrganoGestor { get; set; }
        public string NombreUnidadTramitadora { get; set; }
        public string NombreOrganoProponente { get; set; }
        public string Usuario { get; set; }
        public DateTime? FechaModificacion { get; set; }
        /// <summary>Solo lectura: hay fila guardada.</summary>
        public bool Guardado { get; set; }
        /// <summary>Solo lectura: existe la tabla (Scripts/Issue903_ClientesFacturaElectronica.sql).</summary>
        public bool TablaCreada { get; set; }
    }

    /// <summary>NestoAPI#903: un impuesto de la comprobación.</summary>
    public class ImpuestoComprobacionFacturaeDTO
    {
        public decimal PorcentajeIva { get; set; }
        public decimal BaseImponible { get; set; }
        public decimal CuotaIva { get; set; }
        public decimal PorcentajeRecargo { get; set; }
        public decimal CuotaRecargo { get; set; }
    }

    /// <summary>NestoAPI#903: un vencimiento de la comprobación.</summary>
    public class VencimientoComprobacionFacturaeDTO
    {
        public DateTime Fecha { get; set; }
        public decimal Importe { get; set; }
        public string FormaPago { get; set; }
        /// <summary>PaymentMeans de Facturae (04 transferencia…).</summary>
        public string MedioPago { get; set; }
        public string Iban { get; set; }
    }

    /// <summary>NestoAPI#903: un centro DIR3 de la comprobación.</summary>
    public class CentroComprobacionFacturaeDTO
    {
        public string Rol { get; set; }
        public string Descripcion { get; set; }
        public string Codigo { get; set; }
        public string Nombre { get; set; }
    }

    /// <summary>NestoAPI#903: GET api/Facturas/Facturae?comprobar=true: lo que falta y los totales, para revisar antes de descargar.</summary>
    public class ComprobacionFacturaeDTO
    {
        public string Empresa { get; set; }
        public string Factura { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        /// <summary>Se puede descargar (no falta nada).</summary>
        public bool Listo { get; set; }
        public List<string> Faltas { get; set; } = new List<string>();
        public List<string> Avisos { get; set; } = new List<string>();
        public string NombreFichero { get; set; }
        public bool EsRectificativa { get; set; }
        public string FacturaRectificada { get; set; }
        public string NifEmisor { get; set; }
        public string NifReceptor { get; set; }
        public string NombreReceptor { get; set; }
        /// <summary>«Ficha», «Llamada», «Ficha y llamada» o null.</summary>
        public string OrigenDir3 { get; set; }
        public List<CentroComprobacionFacturaeDTO> Centros { get; set; } = new List<CentroComprobacionFacturaeDTO>();
        public string SuPedido { get; set; }
        public string Expediente { get; set; }
        public string Contrato { get; set; }
        public decimal BaseImponible { get; set; }
        public List<ImpuestoComprobacionFacturaeDTO> Impuestos { get; set; } = new List<ImpuestoComprobacionFacturaeDTO>();
        public decimal TotalImpuestos { get; set; }
        public decimal Total { get; set; }
        public int Lineas { get; set; }
        public List<VencimientoComprobacionFacturaeDTO> Vencimientos { get; set; } = new List<VencimientoComprobacionFacturaeDTO>();
    }

    /// <summary>
    /// NestoAPI#903: factura electrónica para organismos públicos. Descarga de una factura emitida en Facturae 3.2.2 SIN firmar
    /// (para importarla en B2Brouter, que la firma y la manda a FACe) y los DIR3 del organismo por cliente. Solo lectura de la
    /// factura. Administración, Dirección e Informática.
    /// </summary>
    public class FacturaeController : ApiController
    {
        public const string MENSAJE_SIN_PERMISO = "Solo Administración, Dirección o Informática pueden descargar facturas para FACe o cambiar los códigos DIR3 de un cliente.";
        public const string MENSAJE_SIN_TABLA = "Todavía no existe la tabla de los DIR3 de los clientes: hay que lanzar Scripts/Issue903_ClientesFacturaElectronica.sql. Mientras tanto, puede pasar los códigos en la propia descarga.";

        private readonly ServicioFacturae servicio;
        private readonly IRepositorioClientesFacturaElectronica repositorio;

        public FacturaeController()
        {
            var servicioFacturas = new ServicioFacturas();
            repositorio = new RepositorioClientesFacturaElectronica();
            servicio = new ServicioFacturae(new GestorFacturas(servicioFacturas), servicioFacturas, repositorio, servicioFacturas.CargarFacturasRectificadas);
        }

        internal FacturaeController(ServicioFacturae servicio, IRepositorioClientesFacturaElectronica repositorio)
        {
            this.servicio = servicio;
            this.repositorio = repositorio;
        }

        internal static bool TienePermiso(IPrincipal usuario) =>
            usuario != null && !ValidadorAccesoCliente.EsClienteTienda(usuario)
            && (usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.ADMINISTRACION)
                || usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.DIRECCION)
                || usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.INFORMATICA));

        internal static string NombreFichero(string numero) => $"Facturae_{numero?.Trim()}.xml";

        // GET api/Facturas/Facturae?empresa=1&factura=NV2616075[&comprobar=true][&oficinaContable=…&organoGestor=…&unidadTramitadora=…&organoProponente=…][&suPedido=…&expediente=…&contrato=…]
        /// <summary>
        /// La factura emitida en Facturae 3.2.2 sin firmar (application/xml, UTF-8, «Facturae_{número}.xml»). Los DIR3 salen de la
        /// ficha del cliente; los que vengan en la llamada ganan. «Su pedido» sale de la factura (CabFacturaVta.SuPedido) si no se pasa.
        /// Con <paramref name="comprobar"/> devuelve un JSON con lo que falta y los totales. 400 si falta algo; 404 si no existe.
        /// </summary>
        [HttpGet]
        [Authorize]
        [Route("api/Facturas/Facturae")]
        [ResponseType(typeof(ComprobacionFacturaeDTO))]
        public async Task<HttpResponseMessage> GetFacturae(string empresa = null, string factura = null, bool comprobar = false,
            string oficinaContable = null, string organoGestor = null, string unidadTramitadora = null, string organoProponente = null,
            string suPedido = null, string expediente = null, string contrato = null)
        {
            if (!TienePermiso(User))
            {
                return Texto(HttpStatusCode.Forbidden, MENSAJE_SIN_PERMISO);
            }
            if (string.IsNullOrWhiteSpace(factura))
            {
                return Texto(HttpStatusCode.BadRequest, "Falta el número de la factura.");
            }
            string laEmpresa = string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();
            var peticion = new PeticionFacturae
            {
                OficinaContable = oficinaContable,
                OrganoGestor = organoGestor,
                UnidadTramitadora = unidadTramitadora,
                OrganoProponente = organoProponente,
                SuPedido = suPedido,
                Expediente = expediente,
                Contrato = contrato
            };

            PreparacionFacturae preparacion;
            try
            {
                preparacion = await servicio.Preparar(laEmpresa, factura, peticion).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Facturae #903] No se ha podido preparar la factura {factura?.Trim()} (empresa {laEmpresa}): {ex.Message}", ex));
                return Texto(HttpStatusCode.InternalServerError, $"No se ha podido preparar la factura {factura?.Trim()}: {ex.Message}");
            }
            if (preparacion.NoExiste)
            {
                return Texto(HttpStatusCode.NotFound, $"No existe la factura {factura.Trim()} en la empresa {laEmpresa}.");
            }

            ResultadoModeloFacturae resultado = preparacion.Resultado;
            if (comprobar)
            {
                return Request.CreateResponse(HttpStatusCode.OK, Comprobacion(laEmpresa, factura.Trim(), preparacion));
            }
            if (!resultado.Listo)
            {
                return Texto(HttpStatusCode.BadRequest, string.Join(" ", resultado.Faltas));
            }

            byte[] fichero = GeneradorFacturae.GenerarBytes(resultado.Modelo);
            var respuesta = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(fichero) };
            respuesta.Content.Headers.ContentType = new MediaTypeHeaderValue("application/xml") { CharSet = "utf-8" };
            respuesta.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment") { FileName = NombreFichero(factura) };
            return respuesta;
        }

        internal static ComprobacionFacturaeDTO Comprobacion(string empresa, string factura, PreparacionFacturae preparacion)
        {
            ResultadoModeloFacturae resultado = preparacion.Resultado ?? new ResultadoModeloFacturae();
            ModeloFacturae modelo = resultado.Modelo;
            var dto = new ComprobacionFacturaeDTO
            {
                Empresa = empresa,
                Factura = factura,
                Cliente = preparacion.Cliente,
                Contacto = preparacion.Contacto,
                Listo = resultado.Listo,
                Faltas = resultado.Faltas,
                Avisos = resultado.Avisos,
                NombreFichero = NombreFichero(factura),
                OrigenDir3 = preparacion.OrigenDir3
            };
            if (modelo == null)
            {
                return dto;
            }
            TotalesFacturae totales = GeneradorFacturae.Totales(modelo);
            LineaFacturae primera = modelo.Lineas.FirstOrDefault();
            dto.EsRectificativa = modelo.Rectificativa != null;
            dto.FacturaRectificada = modelo.Rectificativa == null ? null : modelo.Rectificativa.SerieOriginal + modelo.Rectificativa.NumeroOriginal;
            dto.NifEmisor = modelo.Emisor?.Nif;
            dto.NifReceptor = modelo.Receptor?.Nif;
            dto.NombreReceptor = modelo.Receptor?.Nombre;
            dto.Centros = modelo.Centros.OrderBy(c => c.Rol).Select(c => new CentroComprobacionFacturaeDTO
            {
                Rol = c.Rol,
                Descripcion = c.Descripcion,
                Codigo = c.Codigo,
                Nombre = c.Nombre
            }).ToList();
            dto.SuPedido = primera?.ReferenciaPedido;
            dto.Expediente = primera?.Expediente;
            dto.Contrato = primera?.Contrato;
            dto.BaseImponible = totales.TotalBruto;
            dto.Impuestos = modelo.Impuestos.OrderByDescending(i => i.TipoIva).Select(i => new ImpuestoComprobacionFacturaeDTO
            {
                PorcentajeIva = i.TipoIva,
                BaseImponible = RoundingHelper.DosDecimalesRound(i.Base),
                CuotaIva = i.Cuota,
                PorcentajeRecargo = i.TipoRecargo,
                CuotaRecargo = i.CuotaRecargo
            }).ToList();
            dto.TotalImpuestos = totales.TotalImpuestos;
            dto.Total = totales.TotalFactura;
            dto.Lineas = modelo.Lineas.Count;
            dto.Vencimientos = modelo.Vencimientos.Select(v => new VencimientoComprobacionFacturaeDTO
            {
                Fecha = v.Fecha,
                Importe = v.Importe,
                FormaPago = v.FormaPagoNesto,
                MedioPago = v.MedioPago,
                Iban = v.Iban
            }).ToList();
            return dto;
        }

        // GET api/Clientes/FacturaElectronica?empresa=1&cliente=12345&contacto=0
        /// <summary>Los DIR3 guardados del cliente y contacto (o de su única fila). Sin fila o sin tabla, 200 con los códigos vacíos.</summary>
        [HttpGet]
        [Authorize]
        [Route("api/Clientes/FacturaElectronica")]
        [ResponseType(typeof(ClienteFacturaElectronicaDTO))]
        public async Task<IHttpActionResult> GetFacturaElectronica(string cliente, string empresa = null, string contacto = null)
        {
            if (!TienePermiso(User))
            {
                return Content(HttpStatusCode.Forbidden, MENSAJE_SIN_PERMISO);
            }
            if (string.IsNullOrWhiteSpace(cliente))
            {
                return BadRequest("Falta el cliente.");
            }
            string laEmpresa = string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();
            string elContacto = contacto?.Trim() ?? string.Empty;
            LecturaClienteFacturaElectronica lectura = await repositorio.Leer(laEmpresa, cliente.Trim(), elContacto).ConfigureAwait(false);
            FilaClienteFacturaElectronica fila = lectura?.Fila;
            if (fila == null)
            {
                return Ok(new ClienteFacturaElectronicaDTO
                {
                    Empresa = laEmpresa,
                    Cliente = cliente.Trim(),
                    Contacto = elContacto,
                    TablaCreada = lectura?.HayTabla == true
                });
            }
            return Ok(new ClienteFacturaElectronicaDTO
            {
                Empresa = fila.Empresa,
                Cliente = fila.Cliente,
                Contacto = fila.Contacto,
                OficinaContable = fila.OficinaContable?.Trim(),
                OrganoGestor = fila.OrganoGestor?.Trim(),
                UnidadTramitadora = fila.UnidadTramitadora?.Trim(),
                OrganoProponente = fila.OrganoProponente?.Trim(),
                NombreOficinaContable = fila.NombreOficinaContable,
                NombreOrganoGestor = fila.NombreOrganoGestor,
                NombreUnidadTramitadora = fila.NombreUnidadTramitadora,
                NombreOrganoProponente = fila.NombreOrganoProponente,
                Usuario = fila.Usuario?.Trim(),
                FechaModificacion = fila.FechaModificacion,
                Guardado = true,
                TablaCreada = true
            });
        }

        // PUT api/Clientes/FacturaElectronica   { "Cliente": "12345", "Contacto": "0", "OficinaContable": "L01280796", … }
        /// <summary>
        /// Guarda los DIR3 del cliente y contacto (oficina contable, órgano gestor y unidad tramitadora obligatorios; órgano
        /// proponente y nombres opcionales). Los tres vacíos = quitarlos. Rastro en Modificaciones. 503 sin la tabla.
        /// </summary>
        [HttpPut]
        [Authorize]
        [Route("api/Clientes/FacturaElectronica")]
        [ResponseType(typeof(ClienteFacturaElectronicaDTO))]
        public async Task<IHttpActionResult> PutFacturaElectronica([FromBody] ClienteFacturaElectronicaDTO peticion)
        {
            if (!TienePermiso(User))
            {
                return Content(HttpStatusCode.Forbidden, MENSAJE_SIN_PERMISO);
            }
            if (peticion == null || string.IsNullOrWhiteSpace(peticion.Cliente))
            {
                return BadRequest("Falta el cliente.");
            }
            var fila = new FilaClienteFacturaElectronica
            {
                Empresa = string.IsNullOrWhiteSpace(peticion.Empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : peticion.Empresa.Trim(),
                Cliente = peticion.Cliente.Trim(),
                Contacto = peticion.Contacto?.Trim() ?? string.Empty
            };
            bool quitar = new[] { peticion.OficinaContable, peticion.OrganoGestor, peticion.UnidadTramitadora, peticion.OrganoProponente }
                .All(string.IsNullOrWhiteSpace);
            if (!quitar)
            {
                var faltas = new List<string>();
                fila.OficinaContable = Codigo(peticion.OficinaContable, "Oficina contable", true, faltas);
                fila.OrganoGestor = Codigo(peticion.OrganoGestor, "Órgano gestor", true, faltas);
                fila.UnidadTramitadora = Codigo(peticion.UnidadTramitadora, "Unidad tramitadora", true, faltas);
                fila.OrganoProponente = Codigo(peticion.OrganoProponente, "Órgano proponente", false, faltas);
                if (faltas.Any())
                {
                    return BadRequest(string.Join(" ", faltas));
                }
                fila.NombreOficinaContable = Nombre(peticion.NombreOficinaContable);
                fila.NombreOrganoGestor = Nombre(peticion.NombreOrganoGestor);
                fila.NombreUnidadTramitadora = Nombre(peticion.NombreUnidadTramitadora);
                fila.NombreOrganoProponente = fila.OrganoProponente == null ? null : Nombre(peticion.NombreOrganoProponente);
            }

            string usuario = UsuarioAuditoriaHelper.Resolver(User, UsuarioAuditoriaHelper.DESCONOCIDO);
            ResultadoEscrituraFacturaElectronica resultado = await repositorio.Guardar(fila, usuario).ConfigureAwait(false);
            if (resultado == ResultadoEscrituraFacturaElectronica.SinTabla)
            {
                return Content(HttpStatusCode.ServiceUnavailable, MENSAJE_SIN_TABLA);
            }
            bool guardado = resultado == ResultadoEscrituraFacturaElectronica.Creado || resultado == ResultadoEscrituraFacturaElectronica.Modificado;
            return Ok(new ClienteFacturaElectronicaDTO
            {
                Empresa = fila.Empresa,
                Cliente = fila.Cliente,
                Contacto = fila.Contacto,
                OficinaContable = fila.OficinaContable,
                OrganoGestor = fila.OrganoGestor,
                UnidadTramitadora = fila.UnidadTramitadora,
                OrganoProponente = fila.OrganoProponente,
                NombreOficinaContable = fila.NombreOficinaContable,
                NombreOrganoGestor = fila.NombreOrganoGestor,
                NombreUnidadTramitadora = fila.NombreUnidadTramitadora,
                NombreOrganoProponente = fila.NombreOrganoProponente,
                Usuario = usuario,
                FechaModificacion = DateTime.Now,
                Guardado = guardado,
                TablaCreada = true
            });
        }

        private static string Codigo(string valor, string que, bool obligatorio, List<string> faltas)
        {
            if (string.IsNullOrWhiteSpace(valor))
            {
                if (obligatorio)
                {
                    faltas.Add(ReglasDir3.Validar(valor, que, out _));
                }
                return null;
            }
            string motivo = ReglasDir3.Validar(valor, que, out string limpio);
            if (motivo != null)
            {
                faltas.Add(motivo);
                return null;
            }
            return limpio;
        }

        private static string Nombre(string nombre)
        {
            string limpio = nombre?.Trim();
            return string.IsNullOrEmpty(limpio) ? null : (limpio.Length > 100 ? limpio.Substring(0, 100) : limpio);
        }

        private static HttpResponseMessage Texto(HttpStatusCode estado, string mensaje) =>
            new HttpResponseMessage(estado) { Content = new StringContent(mensaje ?? string.Empty, System.Text.Encoding.UTF8, "text/plain") };
    }
}
