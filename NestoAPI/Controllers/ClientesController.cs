using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Vendedores;
using NestoAPI.Models;
using NestoAPI.Models.Clientes;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Data.SqlClient;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{

    public class ClientesController : ApiController
    {
        private IServicioVendedores servicioVendedores { get; }
        private readonly NVEntities db = new NVEntities();
        private readonly IGestorClientes _gestorClientes;
        private readonly IGestorSincronizacion _gestorSincronizacion;
        // Carlos 06/07/15: lo pongo para desactivar el Lazy Loading
        public ClientesController(IGestorClientes gestorClientes, IServicioVendedores servicioVendedores, IGestorSincronizacion gestorSincronizacion = null,
            Infraestructure.Clientes.IServicioValidacionNif servicioValidacionNif = null)
        {
            db.Configuration.LazyLoadingEnabled = false;
            this.servicioVendedores = servicioVendedores;
            _gestorClientes = gestorClientes;
            _gestorSincronizacion = gestorSincronizacion ?? new GestorSincronizacion(db);
            // NestoAPI#327: inyectable para tests; por defecto usa el db del controller
            _servicioValidacionNif = servicioValidacionNif ?? new Infraestructure.Clientes.ServicioValidacionNif(db);
        }

        private readonly Infraestructure.Clientes.IServicioValidacionNif _servicioValidacionNif;

        //public ClientesController() : this(null, null)
        //{
        //}

        // POST: api/Clientes/CorregirNif
        // NestoAPI#327 / Nesto#417: "ponerlo en un sitio y se arregla todo". Valida el NIF
        // nuevo contra el censo de la AEAT y, solo si es correcto, lo escribe en TODOS los
        // contactos del cliente, registra la validación (facturar deja de avisar/bloquear) y
        // audita el cambio. Si la AEAT lo rechaza, no se toca nada y se devuelve el motivo.
        [System.Web.Http.Authorize]
        [System.Web.Http.HttpPost]
        [System.Web.Http.Route("api/Clientes/CorregirNif")]
        public async Task<IHttpActionResult> CorregirNif([FromBody] CorregirNifRequest peticion)
        {
            if (peticion == null || string.IsNullOrWhiteSpace(peticion.Cliente) || string.IsNullOrWhiteSpace(peticion.Nif))
            {
                return BadRequest("Hay que indicar el cliente y el NIF nuevo.");
            }

            string usuario = UsuarioAuditoriaHelper.Resolver(User, null);
            Infraestructure.Clientes.ResultadoCorreccionNif resultado =
                await _servicioValidacionNif.CorregirNif(peticion.Cliente, peticion.Nif, usuario);

            if (!resultado.Corregido)
            {
                return BadRequest(resultado.Motivo);
            }
            return Ok(resultado);
        }

        public class CorregirNifRequest
        {
            public string Cliente { get; set; }
            public string Nif { get; set; }
        }

        // POST: api/Clientes/MarcarIdentificacionExtranjera
        // NestoAPI#339: pasaportes y demás identificaciones extranjeras — dejan de validarse
        // contra el censo (no aplica) y las facturas se declaran a Verifactu con IDOtro.
        [System.Web.Http.Authorize]
        [System.Web.Http.HttpPost]
        [System.Web.Http.Route("api/Clientes/MarcarIdentificacionExtranjera")]
        public async Task<IHttpActionResult> MarcarIdentificacionExtranjera([FromBody] MarcarIdentificacionExtranjeraRequest peticion)
        {
            if (peticion == null || string.IsNullOrWhiteSpace(peticion.Cliente))
            {
                return BadRequest("Hay que indicar el cliente, el tipo de identificación y el país.");
            }
            string usuario = UsuarioAuditoriaHelper.Resolver(User, null);
            Infraestructure.Clientes.ResultadoCorreccionNif resultado = await _servicioValidacionNif
                .MarcarIdentificacionExtranjera(peticion.Cliente, peticion.TipoIdentificacion, peticion.Pais, usuario);
            if (!resultado.Corregido)
            {
                return BadRequest(resultado.Motivo);
            }
            return Ok(resultado);
        }

        public class MarcarIdentificacionExtranjeraRequest
        {
            public string Cliente { get; set; }
            /// <summary>Catálogo L7 AEAT: 02 NIF-IVA, 03 pasaporte, 04 doc. del país,
            /// 05 cert. residencia, 06 otro, 07 no censado.</summary>
            public string TipoIdentificacion { get; set; }
            /// <summary>ISO 3166-1 alfa-2 (FR, MA, GB...).</summary>
            public string Pais { get; set; }
        }

        // GET: api/Clientes/NifIncorrectos?vendedor=
        // NestoAPI#327: listado para las pantallas de corrección (Nesto#417 / NestoApp#157).
        // Fichas cuya validación vigente es INCORRECTA, priorizando las que tienen pedido
        // pendiente de servir o facturar. El filtro por vendedor lo aplican los clientes
        // según el rol (administración/dirección sin filtro; vendedor con el suyo). Si el
        // vendedor es JEFE DE EQUIPO, el servidor lo expande a él + su equipo (EquiposVenta),
        // sin que el cliente tenga que saberlo.
        [System.Web.Http.Authorize]
        [System.Web.Http.HttpGet]
        [System.Web.Http.Route("api/Clientes/NifIncorrectos")]
        public async Task<IHttpActionResult> GetNifIncorrectos(string vendedor = null)
        {
            List<string> vendedores = null;
            if (!string.IsNullOrWhiteSpace(vendedor))
            {
                List<string> equipo = await servicioVendedores.VendedoresEquipoString(
                    Constantes.Empresas.EMPRESA_POR_DEFECTO, vendedor);
                vendedores = (equipo ?? new List<string>())
                    .Concat(new[] { vendedor })
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .Select(v => v.Trim())
                    .Distinct()
                    .ToList();
            }
            var lista = await _servicioValidacionNif.ListarNifIncorrectos(vendedores);
            return Ok(lista);
        }

        // GET: api/Clientes
        public async Task<IQueryable<ClienteDTO>> GetClientes(string empresa, string vendedor, string filtro)
        {
            if ((filtro == null) || ((filtro.Length < 4) && (!filtro.All(c => char.IsDigit(c)))))
            {
                throw new Exception("Por favor, utilice un filtro de al menos 4 caracteres");
            }
            List<string> vendedoresLista;
            if (string.IsNullOrWhiteSpace(vendedor))
            {
                vendedoresLista = new List<string>();
            }
            else
            {
                //// Crear una instancia del controlador VendedoresController
                //VendedoresController vendedoresController = new VendedoresController();

                //// Llamar al método GetVendedores con los parámetros deseados
                //var resultado = await vendedoresController.GetVendedores(empresa, vendedor).ConfigureAwait(false);
                List<VendedorDTO> listaVendedores;
                listaVendedores = await servicioVendedores.VendedoresEquipo(empresa, vendedor).ConfigureAwait(false);
                //// Puedes procesar el resultado y devolver la respuesta adecuada
                //if (resultado is OkNegotiatedContentResult<List<VendedorDTO>>)
                //{
                //    listaVendedores = ((OkNegotiatedContentResult<List<VendedorDTO>>)resultado).Content;
                //}
                //else //(resultado is BadRequestErrorMessageResult)
                //{
                //    var mensajeError = ((BadRequestErrorMessageResult)resultado).Message;
                //    // Manejar el error de acuerdo a tus necesidades
                //    throw new Exception(mensajeError);
                //}
                vendedoresLista = listaVendedores.Select(v => v.vendedor).ToList();

            }
            IQueryable<Cliente> clientesVendedor = from c in db.Clientes
                                                   join v in db.VendedoresClientesGruposProductos

                                                   //This is how you join by multiple values
                                                   on new { empresa = c.Empresa, cliente = c.Nº_Cliente, contacto = c.Contacto } equals new { empresa = v.Empresa, cliente = v.Cliente, contacto = v.Contacto }
                                                   into jointData

                                                   //This is how you actually turn the join into a left-join
                                                   from jointRecord in jointData.DefaultIfEmpty()

                                                       //where vendedor == "" || vendedor == null || (c.Empresa == empresa && (c.Vendedor == vendedor || jointRecord.Vendedor == vendedor))
                                                   where vendedor == "" || vendedor == null || vendedoresLista.Contains(c.Vendedor) || (jointRecord != null && vendedoresLista.Contains(jointRecord.Vendedor))
                                                   select c;

            IQueryable<Cliente> clientesTabla = db.Clientes
                .Where(c =>
                c.Empresa == empresa && c.Estado >= 0 &&
                (
                    c.Nº_Cliente.Equals(filtro) ||
                    c.Nombre.Contains(filtro) ||
                    c.Dirección.Contains(filtro) ||
                    c.Teléfono.Contains(filtro) ||
                    c.CIF_NIF.Contains(filtro) ||
                    c.Población.Contains(filtro) ||
                    c.Comentarios.Contains(filtro)
                ))
                .Select(c => c);

            IQueryable<ClienteDTO> clientes = from c in clientesTabla
                                                  //where c.Clientes1.Any(s => clientesVendedor.Contains(s))
                                              where clientesVendedor.Contains(c)
                                              select new ClienteDTO
                                              {
                                                  albaranValorado = c.AlbaranValorado,
                                                  cadena = c.Cadena.Trim(),
                                                  ccc = c.CCC.Trim(),
                                                  cifNif = c.CIF_NIF.Trim(),
                                                  cliente = c.Nº_Cliente.Trim(),
                                                  clientePrincipal = c.ClientePrincipal,
                                                  codigoPostal = c.CodPostal.Trim(),
                                                  comentarioPicking = c.ComentarioPicking.Trim(),
                                                  comentarioRuta = c.ComentarioRuta.Trim(),
                                                  comentarios = c.Comentarios,
                                                  contacto = c.Contacto.Trim(),
                                                  copiasAlbaran = c.NºCopiasAlbarán,
                                                  copiasFactura = c.NºCopiasFactura,
                                                  direccion = c.Dirección.Trim(),
                                                  empresa = c.Empresa.Trim(),
                                                  estado = c.Estado,
                                                  grupo = c.Grupo.Trim(),
                                                  iva = c.IVA.Trim(),
                                                  mantenerJunto = c.MantenerJunto,
                                                  noComisiona = c.NoComisiona,
                                                  nombre = c.Nombre.Trim(),
                                                  periodoFacturacion = c.PeriodoFacturación.Trim(),
                                                  poblacion = c.Población.Trim(),
                                                  provincia = c.Provincia.Trim(),
                                                  ruta = c.Ruta.Trim(),
                                                  servirJunto = c.ServirJunto,
                                                  telefono = c.Teléfono.Trim(),
                                                  vendedor = c.Vendedor.Trim(),
                                                  web = c.Web.Trim()
                                              };

            return clientes.OrderByDescending(o => o.cliente.Equals(filtro));
        }

        // GET: api/Clientes
        public IQueryable<ClienteDTO> GetClientes(string empresa, string filtro)
        {
            // filtro puede llegar como null (p.ej. NestoApp manda ?filtro= al limpiar la búsqueda):
            // se trata como filtro inválido en vez de lanzar NullReferenceException.
            if (string.IsNullOrEmpty(filtro) || (filtro.Length < 4 && !filtro.All(c => char.IsDigit(c))))
            {
                throw new Exception("Por favor, utilice un filtro de al menos 4 caracteres");
            }

            List<ClienteDTO> clientes = db.Clientes
                .Where(c => c.Empresa == empresa && c.Estado >= 0 &&
                (
                    c.Nº_Cliente.Equals(filtro) ||
                    c.Nombre.Contains(filtro) ||
                    c.Dirección.Contains(filtro) ||
                    c.Teléfono.Contains(filtro) ||
                    c.CIF_NIF.Contains(filtro) ||
                    c.Población.Contains(filtro) ||
                    c.Comentarios.Contains(filtro)
                ))
                .Select(clienteEncontrado => new ClienteDTO
                {
                    albaranValorado = clienteEncontrado.AlbaranValorado,
                    cadena = clienteEncontrado.Cadena.Trim(),
                    ccc = clienteEncontrado.CCC.Trim(),
                    cifNif = clienteEncontrado.CIF_NIF.Trim(),
                    cliente = clienteEncontrado.Nº_Cliente.Trim(),
                    clientePrincipal = clienteEncontrado.ClientePrincipal,
                    codigoPostal = clienteEncontrado.CodPostal.Trim(),
                    comentarioPicking = clienteEncontrado.ComentarioPicking.Trim(),
                    comentarioRuta = clienteEncontrado.ComentarioRuta.Trim(),
                    comentarios = clienteEncontrado.Comentarios,
                    contacto = clienteEncontrado.Contacto.Trim(),
                    copiasAlbaran = clienteEncontrado.NºCopiasAlbarán,
                    copiasFactura = clienteEncontrado.NºCopiasFactura,
                    direccion = clienteEncontrado.Dirección.Trim(),
                    empresa = clienteEncontrado.Empresa.Trim(),
                    estado = clienteEncontrado.Estado,
                    grupo = clienteEncontrado.Grupo.Trim(),
                    iva = clienteEncontrado.IVA.Trim(),
                    mantenerJunto = clienteEncontrado.MantenerJunto,
                    noComisiona = clienteEncontrado.NoComisiona,
                    nombre = clienteEncontrado.Nombre.Trim(),
                    periodoFacturacion = clienteEncontrado.PeriodoFacturación.Trim(),
                    poblacion = clienteEncontrado.Población.Trim(),
                    provincia = clienteEncontrado.Provincia.Trim(),
                    ruta = clienteEncontrado.Ruta.Trim(),
                    servirJunto = clienteEncontrado.ServirJunto,
                    telefono = clienteEncontrado.Teléfono.Trim(),
                    vendedor = clienteEncontrado.Vendedor.Trim(),
                    web = clienteEncontrado.Web.Trim()
                }).
                OrderByDescending(o => o.cliente.Equals(filtro))
                .ToList();

            return clientes.AsQueryable();
        }

        // GET: api/Clientes
        public IQueryable<ClienteDTO> GetClientes(string filtro)
        {

            if (filtro.Length < 4)
            {
                throw new Exception("Por favor, utilice un filtro de al menos 4 caracteres");
            }

            List<ClienteDTO> clientes = db.Clientes
                .Where(c => c.Estado >= 0 &&
                (
                    c.Nombre.Contains(filtro) ||
                    c.Dirección.Contains(filtro) ||
                    c.Teléfono.Contains(filtro) ||
                    c.CIF_NIF.Contains(filtro) ||
                    c.Población.Contains(filtro) ||
                    c.Comentarios.Contains(filtro)
                ))
                .Select(clienteEncontrado => new ClienteDTO
                {
                    albaranValorado = clienteEncontrado.AlbaranValorado,
                    cadena = clienteEncontrado.Cadena.Trim(),
                    ccc = clienteEncontrado.CCC.Trim(),
                    cifNif = clienteEncontrado.CIF_NIF.Trim(),
                    cliente = clienteEncontrado.Nº_Cliente.Trim(),
                    clientePrincipal = clienteEncontrado.ClientePrincipal,
                    codigoPostal = clienteEncontrado.CodPostal.Trim(),
                    comentarioPicking = clienteEncontrado.ComentarioPicking.Trim(),
                    comentarioRuta = clienteEncontrado.ComentarioRuta.Trim(),
                    comentarios = clienteEncontrado.Comentarios,
                    contacto = clienteEncontrado.Contacto.Trim(),
                    copiasAlbaran = clienteEncontrado.NºCopiasAlbarán,
                    copiasFactura = clienteEncontrado.NºCopiasFactura,
                    direccion = clienteEncontrado.Dirección.Trim(),
                    empresa = clienteEncontrado.Empresa.Trim(),
                    estado = clienteEncontrado.Estado,
                    grupo = clienteEncontrado.Grupo.Trim(),
                    iva = clienteEncontrado.IVA.Trim(),
                    mantenerJunto = clienteEncontrado.MantenerJunto,
                    noComisiona = clienteEncontrado.NoComisiona,
                    nombre = clienteEncontrado.Nombre.Trim(),
                    periodoFacturacion = clienteEncontrado.PeriodoFacturación.Trim(),
                    poblacion = clienteEncontrado.Población.Trim(),
                    provincia = clienteEncontrado.Provincia.Trim(),
                    ruta = clienteEncontrado.Ruta.Trim(),
                    servirJunto = clienteEncontrado.ServirJunto,
                    telefono = clienteEncontrado.Teléfono.Trim(),
                    vendedor = clienteEncontrado.Vendedor.Trim(),
                    web = clienteEncontrado.Web.Trim()
                }).ToList();

            return clientes.AsQueryable();
        }

        [HttpGet]
        [Route("api/Clientes/GetClientesProbabilidadVenta")]
        // GET: http://localhost:53364/api/Clientes/GetClientesProbabilidadVenta?vendedor=CAM&numeroClientes=10
        [ResponseType(typeof(List<ClienteProbabilidadVenta>))]
        public async Task<IHttpActionResult> GetClientesProbabilidadVenta(string vendedor, int numeroClientes = 20, string tipoInteraccion = "", string grupoSubgrupo = "")
        {
            List<ClienteProbabilidadVenta> respuesta = await _gestorClientes.BuscarClientesPorProbabilidadVenta(vendedor, numeroClientes, tipoInteraccion, grupoSubgrupo);

            return Ok(respuesta);
        }


        // GET: api/Clientes/5
        [ResponseType(typeof(ClienteDTO))]
        public async Task<IHttpActionResult> GetCliente(string empresa, string cliente, string contacto)
        {
            Cliente clienteEncontrado = contacto != null && contacto.Trim() != ""
                ? await (from c in db.Clientes where c.Empresa == empresa && c.Nº_Cliente == cliente && c.Contacto == contacto select c).SingleOrDefaultAsync()
                : await (from c in db.Clientes where c.Empresa == empresa && c.Nº_Cliente == cliente && c.ClientePrincipal select c).SingleOrDefaultAsync();
            if (clienteEncontrado == null)
            {
                return NotFound();
            }

            List<VendedorGrupoProductoDTO> vendedoresGrupoProducto = db.VendedoresClientesGruposProductos.Where(v => v.Empresa == clienteEncontrado.Empresa && v.Cliente == clienteEncontrado.Nº_Cliente && v.Contacto == clienteEncontrado.Contacto)
                .Select(v => new VendedorGrupoProductoDTO
                {
                    vendedor = v.Vendedor,
                    estado = v.Estado,
                    grupoProducto = v.GrupoProducto
                })
                .ToList();

            ClienteDTO clienteDTO = new ClienteDTO
            {
                albaranValorado = clienteEncontrado.AlbaranValorado,
                cadena = clienteEncontrado.Cadena,
                ccc = clienteEncontrado.CCC,
                cifNif = clienteEncontrado.CIF_NIF,
                cliente = clienteEncontrado.Nº_Cliente.Trim(),
                clientePrincipal = clienteEncontrado.ClientePrincipal,
                codigoPostal = clienteEncontrado.CodPostal,
                comentarioPicking = clienteEncontrado.ComentarioPicking,
                comentarioRuta = clienteEncontrado.ComentarioRuta,
                comentarios = clienteEncontrado.Comentarios,
                contacto = clienteEncontrado.Contacto.Trim(),
                copiasAlbaran = clienteEncontrado.NºCopiasAlbarán,
                copiasFactura = clienteEncontrado.NºCopiasFactura,
                direccion = clienteEncontrado.Dirección,
                empresa = clienteEncontrado.Empresa.Trim(),
                estado = clienteEncontrado.Estado,
                grupo = clienteEncontrado.Grupo,
                iva = clienteEncontrado.IVA,
                mantenerJunto = clienteEncontrado.MantenerJunto,
                noComisiona = clienteEncontrado.NoComisiona,
                nombre = clienteEncontrado.Nombre,
                periodoFacturacion = clienteEncontrado.PeriodoFacturación,
                poblacion = clienteEncontrado.Población,
                provincia = clienteEncontrado.Provincia,
                ruta = clienteEncontrado.Ruta,
                servirJunto = clienteEncontrado.ServirJunto,
                telefono = clienteEncontrado.Teléfono,
                vendedor = clienteEncontrado.Vendedor,
                web = clienteEncontrado.Web,
                VendedoresGrupoProducto = vendedoresGrupoProducto,
                // Nesto#340 (1C.8, slice 4): la ficha comercial de Nesto necesita las personas de
                // contacto (grid + correo de agencia) sin cargar la entidad EF en el cliente.
                PersonasContacto = await _gestorClientes.LeerPersonasContacto(
                    clienteEncontrado.Empresa, clienteEncontrado.Nº_Cliente, clienteEncontrado.Contacto)
            };

            return Ok(clienteDTO);
        }

        [HttpGet]
        [Route("api/Clientes/GetClienteCrear")]
        // GET: api/Clientes/5
        [ResponseType(typeof(ClienteCrear))]
        public async Task<IHttpActionResult> GetClienteCrear(string empresa, string cliente, string contacto)
        {
            ClienteCrear respuesta = await _gestorClientes.ConstruirClienteCrear(empresa, cliente, contacto);

            return Ok(respuesta);
        }


        [HttpGet]
        [Route("api/Clientes/CCCs")]
        // GET: api/Clientes/CCCs?empresa=1&cliente=10&contacto=0
        // Carlos 20/11/24: Endpoint para obtener los CCCs de un cliente/contacto para el SelectorCCC
        // Carlos 26/11/24: Modificado para obtener nombre de entidad desde tabla Entidades
        [ResponseType(typeof(List<CCCDTO>))]
        public async Task<IHttpActionResult> GetCCCs(string empresa, string cliente, string contacto)
        {
            if (string.IsNullOrWhiteSpace(empresa))
            {
                return BadRequest("El parámetro 'empresa' es obligatorio");
            }

            if (string.IsNullOrWhiteSpace(cliente))
            {
                return BadRequest("El parámetro 'cliente' es obligatorio");
            }

            if (string.IsNullOrWhiteSpace(contacto))
            {
                return BadRequest("El parámetro 'contacto' es obligatorio");
            }

            // Obtener CCCs completos para poder componer el IBAN
            var cccsDB = await db.CCCs
                .Where(c => c.Empresa == empresa && c.Cliente == cliente && c.Contacto == contacto)
                .OrderByDescending(c => c.Estado)
                .ThenBy(c => c.Número)
                .ToListAsync();

            // Obtener códigos de entidad únicos
            var codigosEntidad = cccsDB
                .Where(c => !string.IsNullOrWhiteSpace(c.Entidad))
                .Select(c => c.Entidad.Trim())
                .Distinct()
                .ToList();

            // Buscar nombres de entidades en la tabla Entidades
            var nombresEntidades = await db.Entidades
                .Where(e => codigosEntidad.Contains(e.Número))
                .ToDictionaryAsync(e => e.Número.Trim(), e => e.Descripción != null ? e.Descripción.Trim() : null);

            // Convertir a DTOs y generar IBAN formateado
            List<CCCDTO> cccs = cccsDB.Select(c =>
            {
                string ibanFormateado = null;
                try
                {
                    // Componer el IBAN a partir de los campos del CCC
                    string ibanCompleto = Models.Clientes.Iban.ComponerIban(c);
                    if (!string.IsNullOrWhiteSpace(ibanCompleto))
                    {
                        var iban = new Models.Clientes.Iban(ibanCompleto);
                        ibanFormateado = iban.Formateado;
                    }
                }
                catch
                {
                    // Si falla la validación del IBAN, dejarlo null
                }

                string codigoEntidad = c.Entidad != null ? c.Entidad.Trim() : null;
                string nombreEntidad = null;
                if (codigoEntidad != null && nombresEntidades.TryGetValue(codigoEntidad, out var nombre))
                {
                    nombreEntidad = nombre;
                }

                return new CCCDTO
                {
                    empresa = c.Empresa.Trim(),
                    cliente = c.Cliente.Trim(),
                    contacto = c.Contacto.Trim(),
                    numero = c.Número.Trim(),
                    pais = c.Pais != null ? c.Pais.Trim() : null,
                    entidad = codigoEntidad,
                    oficina = c.Oficina != null ? c.Oficina.Trim() : null,
                    bic = c.BIC != null ? c.BIC.Trim() : null,
                    estado = c.Estado,
                    tipoMandato = c.TipoMandato,
                    fechaMandato = c.FechaMandato,
                    ibanFormateado = ibanFormateado,
                    nombreEntidad = nombreEntidad,
                    dcIban = c.DC_IBAN?.Trim(),
                    dc = c.DC?.Trim(),
                    numeroCuenta = c.Nº_Cuenta?.Trim(),
                    secuencia = c.Secuencia?.Trim()
                };
            }).ToList();

            return Ok(cccs);
        }

        [HttpGet]
        [Route("api/Clientes/EstadosCCC")]
        // GET: api/Clientes/EstadosCCC?empresa=1
        // 1C.8 slice 5: catálogo de estados de CCC para el combo de la ficha de clientes
        [ResponseType(typeof(List<EstadoCCCDTO>))]
        public async Task<IHttpActionResult> GetEstadosCCC(string empresa)
        {
            if (string.IsNullOrWhiteSpace(empresa))
            {
                return BadRequest("El parámetro 'empresa' es obligatorio");
            }

            List<EstadoCCCDTO> estados = await _gestorClientes.LeerEstadosCCC(empresa);
            return Ok(estados);
        }

        [HttpPut]
        [Route("api/Clientes/CCCs")]
        // PUT: api/Clientes/CCCs
        // 1C.8 slice 5: guardado del CRUD de CCCs de la ficha de clientes (upsert). Devuelve
        // los efectos pendientes y pedidos abiertos que apuntan a un CCC distinto del activo,
        // para que el cliente muestre los avisos que antes calculaba con EF.
        [ResponseType(typeof(GuardarCCCsRespuesta))]
        public async Task<IHttpActionResult> PutCCCs(GuardarCCCsRequest peticion)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            string usuario = UsuarioAuditoriaHelper.Resolver(User, null);
            GuardarCCCsRespuesta respuesta;
            try
            {
                respuesta = await _gestorClientes.GuardarCCCs(db, peticion, usuario);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }

            _ = await db.SaveChangesAsync();

            return Ok(respuesta);
        }

        [HttpGet]
        [Route("api/Clientes/ComprobarNifNombre")]
        // GET: api/Clientes/5
        [ResponseType(typeof(RespuestaNifNombreCliente))]
        public async Task<IHttpActionResult> ComprobarNifNombre(string nif, string nombre)
        {
            RespuestaNifNombreCliente respuesta = await _gestorClientes.ComprobarNifNombre(nif, nombre);

            return Ok(respuesta);
        }

        [HttpGet]
        [Route("api/Clientes/ComprobarDatosGenerales")]
        // GET: api/Clientes/5
        [ResponseType(typeof(RespuestaDatosGeneralesClientes))]
        // NestoAPI#306: direccionVerificada=true cuando dirección y CP vienen del combo de Places
        // (ya son consistentes) → se salta el geocoding y solo normaliza para la BD.
        public async Task<IHttpActionResult> ComprobarDatosGenerales(string direccion, string codigoPostal, string telefono, bool direccionVerificada = false)
        {
            RespuestaDatosGeneralesClientes respuesta = await _gestorClientes.ComprobarDatosGenerales(direccion, codigoPostal, telefono, direccionVerificada);

            return Ok(respuesta);
        }

        [HttpGet]
        [Route("api/Clientes/GetClientePorEmail")]
        // GET: api/Clientes/5
        [ResponseType(typeof(ClienteTelefonoLookup))]
        public async Task<IHttpActionResult> GetClientePorEmail(string email)
        {
            ClienteTelefonoLookup respuesta = await _gestorClientes.BuscarClientePorEmail(email);

            return Ok(respuesta);
        }

        [HttpGet]
        [Route("api/Clientes/GetClientePorEmailNif")]
        // GET: api/Clientes/5
        [ResponseType(typeof(ClienteDTO))]
        public async Task<IHttpActionResult> GetClientePorEmailNif(string email, string nif)
        {
            ClienteDTO respuesta = await _gestorClientes.BuscarClientePorEmailNif(email, nif);

            return Ok(respuesta);
        }

        [HttpGet]
        [Route("api/Clientes/ComprobarDatosBanco")]
        // GET: api/Clientes/5
        [ResponseType(typeof(RespuestaDatosBancoCliente))]
        public IHttpActionResult ComprobarDatosBanco(string formaPago, string plazosPago, string iban)
        {
            RespuestaDatosBancoCliente respuesta = _gestorClientes.ComprobarDatosBanco(formaPago, plazosPago, iban);

            return Ok(respuesta);
        }

        [HttpPut]
        [Route("api/Clientes/ClienteComercial")]
        // PUT: api/Clientes/5
        [ResponseType(typeof(void))]
        public async Task<IHttpActionResult> PutCliente(ClienteDTO cliente)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            Cliente clienteDB = db.Clientes.SingleOrDefault(c => c.Empresa == cliente.empresa && c.Nº_Cliente == cliente.cliente && c.Contacto == cliente.contacto);


            if (clienteDB == null || cliente == null)
            {
                return BadRequest();
            }

            // Cambiamos los vendedores
            if (clienteDB.Vendedor != null && cliente.vendedor != null && clienteDB.Vendedor.Trim() != cliente.vendedor.Trim())
            {
                clienteDB.Vendedor = cliente.vendedor;
                clienteDB.Estado = cliente.estado;
                clienteDB.Usuario = cliente.usuario;
            }

            db.Entry(clienteDB).State = System.Data.Entity.EntityState.Modified;

            // Carlos 02/03/17: gestionamos el vendedor por grupo de producto
            GestorComisiones.ActualizarVendedorClienteGrupoProducto(db, clienteDB, cliente);

            try
            {
                _ = await db.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!ClienteExists(cliente.empresa, cliente.cliente, cliente.contacto))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }
            catch (Exception ex)
            {
                throw new Exception("No se ha podido actualizar el cliente", ex);
            }

            return StatusCode(HttpStatusCode.NoContent);
        }

        [HttpPut]
        [Route("api/Clientes/DejarDeVisitar")]
        // PUT: api/Clientes/5
        [ResponseType(typeof(void))]
        public async Task<IHttpActionResult> DejarDeVisitar(ClienteCrear cliente)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }


            try
            {
                List<Cliente> clientesDB = await _gestorClientes.DejarDeVisitar(db, cliente);
                foreach (Cliente clienteDB in clientesDB)
                {
                    db.Entry(clienteDB).State = System.Data.Entity.EntityState.Modified;
                }

                _ = await db.SaveChangesAsync();
            }
            catch (NotFoundException ex)
            {
                // Issue #283: cliente/contacto inexistente -> 404 con mensaje claro (antes era
                // un 500 'Sequence contains no elements' del SingleAsync).
                return Content(HttpStatusCode.NotFound, ex.Message);
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!ClienteExists(cliente.Empresa, cliente.Cliente, cliente.Contacto))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }

            return StatusCode(HttpStatusCode.NoContent);
        }

        // PUT: api/Clientes/5
        [ResponseType(typeof(Cliente))]
        public async Task<IHttpActionResult> PutCliente(ClienteCrear clienteCrear)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }


            try
            {
                Cliente cliente = await _gestorClientes.ModificarCliente(clienteCrear, db);
                return CreatedAtRoute("DefaultApi",
                    new { cliente.Empresa, cliente.Nº_Cliente, cliente.Contacto },
                    cliente);
            }
            catch (NotFoundException)
            {
                return NotFound();
            }
        }


        // POST: api/Clientes
        [ResponseType(typeof(Cliente))]
        public async Task<IHttpActionResult> PostCliente(ClienteCrear clienteCrear)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            try
            {
                Cliente cliente = await _gestorClientes.CrearCliente(clienteCrear, db);
                return CreatedAtRoute("DefaultApi",
                    new { cliente.Empresa, cliente.Nº_Cliente, cliente.Contacto },
                    cliente);
            }
            catch (ConflictException)
            {
                return Conflict();
            }
            catch (ValidationException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        //// POST: api/Clientes
        //[ResponseType(typeof(Cliente))]
        //public async Task<IHttpActionResult> PostCliente(Cliente cliente)
        //{
        //    if (!ModelState.IsValid)
        //    {
        //        return BadRequest(ModelState);
        //    }

        //    db.Clientes.Add(cliente);

        //    try
        //    {
        //        await db.SaveChangesAsync();
        //    }
        //    catch (DbUpdateException)
        //    {
        //        if (ClienteExists(cliente.Empresa, cliente.Nº_Cliente, cliente.Contacto))
        //        {
        //            return Conflict();
        //        }
        //        else
        //        {
        //            throw;
        //        }
        //    }

        //    return CreatedAtRoute("DefaultApi", new { cliente.Empresa, cliente.Nº_Cliente, cliente.Contacto }, cliente);
        //}

        //// DELETE: api/Clientes/5
        //[ResponseType(typeof(Cliente))]
        //public async Task<IHttpActionResult> DeleteCliente(string id)
        //{
        //    Cliente cliente = await db.Clientes.FindAsync(id);
        //    if (cliente == null)
        //    {
        //        return NotFound();
        //    }

        //    db.Clientes.Remove(cliente);
        //    await db.SaveChangesAsync();

        //    return Ok(cliente);
        //}

        [HttpGet]
        [Route("api/Clientes/MandatoPDF")]
        //http://localhost:53364/api/Clientes/MandatoPDF?empresa=1&cliente=1&contacto=1&ccc=1
        public async Task<HttpResponseMessage> GetMandato(string empresa, string cliente, string contacto, string ccc)
        {
            Mandato mandato = await _gestorClientes.LeerMandato(empresa, cliente, contacto, ccc).ConfigureAwait(true);
            if (!mandato.Iban.EsValido)
            {
                throw new Exception($"El IBAN {mandato.Iban.Formateado} no es válido");
            }
            List<Mandato> mandatos = new List<Mandato> { mandato };

            HttpResponseMessage result = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = _gestorClientes.MandatoEnPDF(mandatos)
            };
            //result.Content.Headers.ContentDisposition =
            //    new ContentDispositionHeaderValue("attachment")
            //    {
            //        FileName = factura.Item2 + ".pdf"
            //    };
            result.Content.Headers.ContentType =
                new MediaTypeHeaderValue("application/pdf");

            return result;
        }


        [HttpGet]
        [Route("api/Clientes/Sync")]
        [ResponseType(typeof(bool))]
        public async Task<IHttpActionResult> GetClientesSync(string vendedor)
        {
            List<Cliente> clientes = await db.Clientes
                .Where(c => c.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && c.Vendedor == vendedor && c.Estado >= Constantes.Clientes.Estados.VISITA_PRESENCIAL)
                .OrderBy(c => c.Nº_Cliente)
                .ThenByDescending(c => c.ClientePrincipal)
                .ThenBy(c => c.Contacto)
                .Include(c => c.PersonasContactoClientes1)
                .ToListAsync();

            bool todosOK = true;
            int batchSize = 50; // Tamaño del lote
            int totalClientes = clientes.Count;
            int delayMs = 5000; // Pausa de 5 segundos entre lotes

            // Procesar por lotes de 50
            for (int i = 0; i < totalClientes; i += batchSize)
            {
                List<Cliente> lote = clientes.Skip(i).Take(batchSize).ToList();

                foreach (Cliente cliente in lote)
                {
                    try
                    {
                        await _gestorClientes.PublicarClienteSincronizar(cliente, "Nesto viejo");
                    }
                    catch
                    {
                        todosOK = false;
                    }
                }

                // Esperar antes de procesar el siguiente lote (si no es el último)
                if (i + batchSize < totalClientes)
                {
                    await Task.Delay(delayMs);
                }
            }

            return Ok(todosOK);
        }

        [HttpGet]
        [Route("api/Clientes/Sync")]
        [ResponseType(typeof(bool))]
        public async Task<IHttpActionResult> GetClientesSync()
        {
            bool resultado = await _gestorSincronizacion.ProcesarTabla(
                tabla: "Clientes",
                obtenerEntidades: async (registro) =>
                {
                    // Buscar todos los contactos del cliente en la base de datos
                    return await db.Clientes
                        .Where(c => c.Nº_Cliente == registro.ModificadoId && c.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO)
                        .OrderBy(c => c.Nº_Cliente)
                        .ThenByDescending(c => c.ClientePrincipal)
                        .ThenBy(c => c.Contacto)
                        .Include(c => c.PersonasContactoClientes1)
                        .ToListAsync();
                },
                publicarEntidad: async (cliente, usuario) =>
                {
                    await _gestorClientes.PublicarClienteSincronizar(cliente, "Nesto viejo", usuario);
                }
            );

            return Ok(resultado);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                db.Dispose();
            }
            base.Dispose(disposing);
        }


        private bool ClienteExists(string empresa, string numCliente, string contacto)
        {
            return db.Clientes.Count(e => e.Empresa == empresa && e.Nº_Cliente == numCliente && e.Contacto == contacto) > 0;
        }
    }
}