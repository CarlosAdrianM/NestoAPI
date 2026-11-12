using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NestoAPI.Infraestructure.Agencias.RepartoPropio;
using NestoAPI.Infrastructure;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;

namespace NestoAPI.Infraestructure.NotasEntrega
{
    /// <summary>NestoAPI#855: los estados de una recogida solicitada. Los tres primeros se guardan; los demás se calculan al leer.</summary>
    public static class EstadosRecogidaSolicitada
    {
        public const string SOLICITADA = "Solicitada";
        public const string CONFIRMADA = "Confirmada";
        public const string RECHAZADA = "Rechazada";
        public const string ANULADA = "Anulada";
        /// <summary>Calculado: su parada (envío de la agencia 14) está en el reparto (Tramitado o Incidentado).</summary>
        public const string EN_REPARTO = "EnReparto";
        /// <summary>Calculado: parada entregada (recogida), bultos NR recogidos o abono hecho (la lectura en Algete).</summary>
        public const string RECOGIDA = "Recogida";
    }

    /// <summary>NestoAPI#855 (matiz de Carlos 12/11/26): quién pasa por el cliente a recogerla.</summary>
    public static class QuienRecogeRecogida
    {
        /// <summary>La furgoneta (parada del reparto propio): solo contactos de ruta propia.</summary>
        public const string REPARTO = "Reparto";
        /// <summary>El propio vendedor: cualquier contacto; no hay parada y se cierra al leer el NR en Algete.</summary>
        public const string VENDEDOR = "Vendedor";

        /// <summary>Null o vacío → Reparto; «vendedor» → Vendedor; cualquier otra cosa → null (no vale).</summary>
        public static string Normalizar(string valor)
        {
            string v = valor?.Trim();
            if (string.IsNullOrEmpty(v) || string.Equals(v, REPARTO, StringComparison.OrdinalIgnoreCase))
            {
                return REPARTO;
            }
            return string.Equals(v, VENDEDOR, StringComparison.OrdinalIgnoreCase) ? VENDEDOR : null;
        }
    }

    /// <summary>NestoAPI#855: una fila de dbo.RecogidasSolicitadas (Scripts/Issue855_RecogidasSolicitadas.sql).</summary>
    public class FilaRecogidaSolicitada
    {
        public int Id { get; set; }
        public string Empresa { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string Texto { get; set; }
        public short Bultos { get; set; }
        public string FacturaOriginal { get; set; }
        public string QuienRecoge { get; set; }
        public string Vendedor { get; set; }
        public string UsuarioSolicita { get; set; }
        public DateTime FechaSolicitud { get; set; }
        public string Estado { get; set; }
        public int? PedidoNota { get; set; }
        public string UsuarioResuelve { get; set; }
        public DateTime? FechaResolucion { get; set; }
        public string MotivoRechazo { get; set; }
        public string UsuarioAnula { get; set; }
        public DateTime? FechaAnulacion { get; set; }
        public string MotivoAnulacion { get; set; }
        public DateTime? AvisoSinRespuesta { get; set; }
    }

    /// <summary>NestoAPI#855: cómo va la nota de recogida de una solicitud confirmada.</summary>
    public class SeguimientoNotaRecogida
    {
        /// <summary>El pedido de la nota sigue existiendo.</summary>
        public bool Existe { get; set; }
        /// <summary>Alguna línea con picking o ya procesada (Estado distinto de 1): ya está en marcha en el almacén.</summary>
        public bool Empezada { get; set; }
        /// <summary>Algún envío del pedido (de cualquier agencia) en curso, tramitado, entregado…</summary>
        public bool TieneEnvio { get; set; }
        /// <summary>El estado del último envío de la agencia 14 (la parada), o null.</summary>
        public short? EstadoParada { get; set; }
        /// <summary>NotasEntregaCabecera.PedidoAbono: la recogida ya se ha leído en Algete.</summary>
        public int? PedidoAbono { get; set; }
        /// <summary>Algún bulto NR{pedido}-{N} recogido o descargado en la furgoneta (RepartoBultosMovimientos).</summary>
        public bool BultosRecogidos { get; set; }
    }

    /// <summary>Quién hace la petición (lo que el servicio necesita del token).</summary>
    public class UsuarioRecogidas
    {
        /// <summary>El usuario del token (con dominio en Nesto, el de Identity en NestoApp).</summary>
        public string Nombre { get; set; }
        /// <summary>El claim «Vendedor», o null.</summary>
        public string Vendedor { get; set; }
        /// <summary>Almacén, Administración, Dirección o Informática (confirmar, rechazar y anular mientras no esté en reparto).</summary>
        public bool PuedeGestionar { get; set; }
        /// <summary>Almacén o Administración: anular cuando ya está en reparto (decisión de Carlos 12/11/26).</summary>
        public bool PuedeAnularEnReparto { get; set; }
    }

    public interface IRepositorioRecogidasSolicitadas
    {
        /// <summary>¿Se ha lanzado Scripts/Issue855_RecogidasSolicitadas.sql (tabla y columnas)?</summary>
        bool ScriptAplicado();
        Task<FilaRecogidaSolicitada> Leer(int id);
        /// <summary>Las del cliente (y contacto, si viene), de la más reciente a la más antigua (como mucho 50).</summary>
        Task<List<FilaRecogidaSolicitada>> LeerDelCliente(string empresa, string cliente, string contacto);
        /// <summary>Las «Solicitada» (de la empresa, si viene), de la más antigua a la más reciente.</summary>
        Task<List<FilaRecogidaSolicitada>> LeerPendientes(string empresa);
        /// <summary>Las «Solicitada» de las que todavía no se ha avisado por falta de respuesta.</summary>
        Task<List<FilaRecogidaSolicitada>> LeerSinAvisar();
        /// <summary>Los Id de las «Solicitada» de ese contacto.</summary>
        Task<List<int>> LeerAbiertas(string empresa, string cliente, string contacto);
        Task<FichaNotaCobro> LeerFicha(string empresa, string cliente, string contacto);
        /// <summary>Las fichas de esos clientes, por «cliente/contacto» (recortados).</summary>
        Task<Dictionary<string, FichaNotaCobro>> LeerFichas(string empresa, IReadOnlyCollection<string> clientes);
        /// <summary>El cliente de la factura (recortado), o null si no existe.</summary>
        Task<string> LeerClienteFactura(string empresa, string factura);
        /// <summary>Inserta la solicitud y devuelve su Id.</summary>
        Task<int> Insertar(FilaRecogidaSolicitada fila);
        /// <summary>
        /// En UNA transacción: el pedido de la nota de recogida (NotaEntrega = 1, líneas de texto), su NotasEntregaCabecera (Recogida,
        /// bultos, factura original) y la solicitud a «Confirmada» con el pedido. Null (y nada guardado) si la solicitud ya no estaba
        /// «Solicitada» (otro la ha resuelto a la vez).
        /// </summary>
        Task<int?> Confirmar(FilaRecogidaSolicitada fila, short bultos, string texto, IReadOnlyList<string> lineas, FichaNotaCobro ficha, string usuario);
        /// <summary>A «Rechazada» si sigue «Solicitada». False si no.</summary>
        Task<bool> Rechazar(int id, string motivo, string usuario);
        /// <summary>A «Anulada» si sigue en <paramref name="estadoEsperado"/>. False si no.</summary>
        Task<bool> Anular(int id, string estadoEsperado, string motivo, string usuario);
        /// <summary>
        /// En UNA transacción, borra el pedido de la nota (como Borrar pedido de Nesto) y pasa la solicitud a «Anulada», solo si la nota
        /// sigue sin empezar (sin picking, sin procesar, sin envío y sin abono) y la solicitud sigue «Confirmada». False (y nada
        /// cambiado) si no.
        /// </summary>
        Task<bool> AnularBorrandoNota(int id, string empresa, int pedido, string motivo, string usuario);
        Task<SeguimientoNotaRecogida> LeerSeguimiento(string empresa, int pedido);
        /// <summary>Apunta que ya se avisó por falta de respuesta. False si ya estaba apuntado o ya no está «Solicitada».</summary>
        Task<bool> MarcarAvisoSinRespuesta(int id);
        /// <summary>¿Esa nota de recogida es la de una solicitud confirmada que recoge el vendedor? False sin el script.</summary>
        Task<bool> EsRecogidaDelVendedor(string empresa, int pedidoNota);
    }

    /// <summary>NestoAPI#855: los avisos (campana, push y AlmacenHub). Nunca lanzan ni hacen esperar a la petición.</summary>
    public interface IAvisosRecogidasSolicitadas
    {
        /// <summary>Campana a Almacén (RecogidaSolicitada) y haCambiado al grupo RecogidasSolicitadas del AlmacenHub.</summary>
        void Solicitada(RecogidaSolicitadaDTO recogida, string usuario);
        /// <summary>Push de NestoApp y campana a quien la pidió (RecogidaSolicitadaResuelta) y haCambiado.</summary>
        void Resuelta(RecogidaSolicitadaDTO recogida, string usuario);
        /// <summary>haCambiado; con <paramref name="avisarAlmacen"/> (anulada ya en reparto), campana a Almacén; si la anula otro, a quien la pidió.</summary>
        void Anulada(RecogidaSolicitadaDTO recogida, string usuario, bool avisarAlmacen);
        /// <summary>Campana a Almacén y a quien la pidió (RecogidaSolicitadaSinRespuesta). Para el job: espera a que acabe; nunca lanza.</summary>
        Task SinRespuesta(RecogidaSolicitadaDTO recogida);
    }

    /// <summary>
    /// NestoAPI#855: quién puede qué. Pedir, ver las de un cliente y anular las suyas: los vendedores de NestoApp (solo sus clientes,
    /// <see cref="Seguridad.ClientesDelVendedor"/>) y los empleados (Nesto); nunca los clientes de la tienda. Confirmar, rechazar y ver
    /// las pendientes: los cuatro grupos de las notas (<see cref="PermisosNotasEntrega"/>). Anular una que ya está en reparto: Almacén
    /// o Administración.
    /// </summary>
    public static class PermisosRecogidasSolicitadas
    {
        public static readonly IReadOnlyList<string> GRUPOS_ANULAR_EN_REPARTO = new[]
        {
            Constantes.GruposSeguridad.ALMACEN, Constantes.GruposSeguridad.ADMINISTRACION
        };

        public static bool PuedeAnularEnReparto(System.Security.Principal.IPrincipal usuario)
            => usuario != null && GRUPOS_ANULAR_EN_REPARTO.Any(g => usuario.IsInRoleSinDominio(g));

        /// <summary>Vendedor de NestoApp, empleado o de los grupos de notas; un cliente de la tienda, no.</summary>
        public static bool PuedeSolicitar(System.Security.Principal.IPrincipal usuario)
        {
            var identidad = usuario?.Identity as System.Security.Claims.ClaimsIdentity;
            if (identidad == null || !identidad.IsAuthenticated)
            {
                return false;
            }
            if (!string.IsNullOrWhiteSpace(identidad.FindFirst("cliente")?.Value))
            {
                return false;
            }
            return Seguridad.ClientesDelVendedor.EsVendedorDeNestoApp(identidad)
                || identidad.FindFirst("IsEmployee")?.Value == "true"
                || PermisosNotasEntrega.Puede(usuario);
        }

        /// <summary>
        /// <see cref="PuedeSolicitar"/> y, si es un vendedor de NestoApp, que el cliente sea suyo (empresa, usuario, vendedor, cliente →
        /// <paramref name="esDelVendedor"/>). Sin cliente, false.
        /// </summary>
        public static bool PuedeCliente(System.Security.Principal.IPrincipal usuario, string empresa, string cliente,
            Func<string, string, string, string, bool> esDelVendedor)
        {
            if (string.IsNullOrWhiteSpace(cliente) || !PuedeSolicitar(usuario))
            {
                return false;
            }
            var identidad = (System.Security.Claims.ClaimsIdentity)usuario.Identity;
            if (!Seguridad.ClientesDelVendedor.EsVendedorDeNestoApp(identidad))
            {
                return true;
            }
            string emp = string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();
            return Seguridad.ClientesDelVendedor.PuedeElVendedor(identidad, emp, cliente.Trim(), esDelVendedor);
        }
    }

    /// <summary>NestoAPI#855: lo que contesta el servicio; el controlador lo pasa tal cual (código HTTP, mensaje de usted, contenido).</summary>
    public class ResultadoRecogidaSolicitada<T>
    {
        public HttpStatusCode Codigo { get; private set; }
        public string Mensaje { get; private set; }
        public T Contenido { get; private set; }
        public bool EsOk => Codigo == HttpStatusCode.OK || Codigo == HttpStatusCode.Created;

        public static ResultadoRecogidaSolicitada<T> Bien(T contenido, HttpStatusCode codigo = HttpStatusCode.OK)
            => new ResultadoRecogidaSolicitada<T> { Codigo = codigo, Contenido = contenido };

        public static ResultadoRecogidaSolicitada<T> Mal(HttpStatusCode codigo, string mensaje)
            => new ResultadoRecogidaSolicitada<T> { Codigo = codigo, Mensaje = mensaje };
    }

    /// <summary>
    /// NestoAPI#855 (gemelas Nesto#635 y NestoApp#259): el vendedor PIDE una recogida desde la ficha del cliente y el almacén la
    /// CONFIRMA (nace la nota de recogida NR{pedido}, por el mismo camino que #757: pedido con NotaEntrega = 1, tipo Recogida, bultos,
    /// factura original y líneas de texto) o la RECHAZA con motivo. Decisiones de Carlos (12/11/26):
    /// <list type="bullet">
    /// <item>Recoge el REPARTO (solo contactos de ruta propia, la regla de la nota de cobro) o el propio VENDEDOR (cualquier
    /// contacto: es el caso en que la furgoneta no va). Con Vendedor la nota nace con la fecha de entrega «sin determinar»
    /// (31/12/2099, como las notas automáticas de #582): no entra en el picking ni en Facturar rutas, así que no hay parada.</item>
    /// <item>En los dos casos el cierre es el mismo: leer en Algete el NR del papel de la ruta o del móvil del vendedor (el abono
    /// de la recogida, POST api/NotasEntrega/Recogidas/Abono, que a una del vendedor ya no le pide la parada recogida).</item>
    /// <item>Anular: quien la pidió (o los cuatro grupos de notas) mientras no esté en reparto (sin parada, sin picking, sin
    /// abono): si ya hay nota, se borra. Después, solo Almacén o Administración: se marca anulada y se avisa a Almacén para que
    /// quite la parada a mano (la nota no se toca: borrarla con su parada o su picking en marcha no es seguro).</item>
    /// <item>Un día laborable (Algete, <see cref="Models.RecursosHumanos.GestorFestivos"/>) sin respuesta: campana a Almacén y a
    /// quien la pidió, una sola vez (job recogidas-solicitadas-sin-respuesta).</item>
    /// </list>
    /// </summary>
    public class ServicioRecogidasSolicitadas
    {
        public const string MENSAJE_FALTA_SCRIPT = "Las recogidas solicitadas aún no están disponibles: falta lanzar el script " +
            "Scripts/Issue855_RecogidasSolicitadas.sql. Avise a Informática.";
        public const string TABLA_MODIFICACIONES = "Pedidos";
        internal const int MAXIMO_TEXTO = 500;
        internal const int MAXIMO_MOTIVO = 300;
        internal const int LONGITUD_LINEA_TEXTO = 50;
        internal const int LONGITUD_USUARIO_PEDIDO = 30;

        private readonly IRepositorioRecogidasSolicitadas repositorio;
        private readonly IAvisosRecogidasSolicitadas avisos;
        private readonly Func<bool> repartoPropioActivo;
        private readonly Func<DateTime> ahora;
        private readonly Func<DateTime, bool> esFestivo;

        /// <param name="esFestivo">Día no laborable en Algete (sábados, domingos y festivos). Por defecto, GestorFestivos.</param>
        public ServicioRecogidasSolicitadas(IRepositorioRecogidasSolicitadas repositorio, IAvisosRecogidasSolicitadas avisos,
            Func<bool> repartoPropioActivo = null, Func<DateTime> ahora = null, Func<DateTime, bool> esFestivo = null)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
            this.avisos = avisos;
            this.repartoPropioActivo = repartoPropioActivo ?? InterruptorRepartoPropio.EstaActivo;
            this.ahora = ahora ?? (() => DateTime.Now);
            this.esFestivo = esFestivo ?? (d => Models.RecursosHumanos.GestorFestivos.EsFestivo(d, Constantes.Almacenes.ALGETE));
        }

        // ================================================================ solicitar (vendedor)

        public async Task<ResultadoRecogidaSolicitada<RecogidaSolicitadaDTO>> Solicitar(SolicitarRecogidaDTO peticion, UsuarioRecogidas usuario)
        {
            if (!repositorio.ScriptAplicado())
            {
                return Mal<RecogidaSolicitadaDTO>(HttpStatusCode.ServiceUnavailable, MENSAJE_FALTA_SCRIPT);
            }
            if (peticion == null || string.IsNullOrWhiteSpace(peticion.Cliente) || string.IsNullOrWhiteSpace(peticion.Contacto))
            {
                return Mal<RecogidaSolicitadaDTO>(HttpStatusCode.BadRequest, "Indique el cliente y el contacto (la dirección) donde hay que recoger.");
            }
            string empresa = Empresa(peticion.Empresa);
            if (empresa != Constantes.Empresas.EMPRESA_POR_DEFECTO)
            {
                return Mal<RecogidaSolicitadaDTO>(HttpStatusCode.BadRequest, "Las recogidas se piden en la empresa 1.");
            }
            string texto = peticion.Texto?.Trim();
            string errorTexto = ValidarTexto(texto);
            if (errorTexto != null)
            {
                return Mal<RecogidaSolicitadaDTO>(HttpStatusCode.BadRequest, errorTexto);
            }
            short bultos = peticion.Bultos ?? TiposNotaEntrega.BULTOS_POR_DEFECTO;
            string errorBultos = ValidarBultos(bultos);
            if (errorBultos != null)
            {
                return Mal<RecogidaSolicitadaDTO>(HttpStatusCode.BadRequest, errorBultos);
            }
            string quienRecoge = QuienRecogeRecogida.Normalizar(peticion.QuienRecoge);
            if (quienRecoge == null)
            {
                return Mal<RecogidaSolicitadaDTO>(HttpStatusCode.BadRequest,
                    $"Quién recoge tiene que ser «{QuienRecogeRecogida.REPARTO}» o «{QuienRecogeRecogida.VENDEDOR}», no «{peticion.QuienRecoge.Trim()}».");
            }
            string cliente = peticion.Cliente.Trim();
            string contacto = peticion.Contacto.Trim();
            FichaNotaCobro ficha = await repositorio.LeerFicha(empresa, cliente, contacto).ConfigureAwait(false);
            if (ficha == null)
            {
                return Mal<RecogidaSolicitadaDTO>(HttpStatusCode.NotFound, $"No existe el cliente {cliente}/{contacto}.");
            }
            if (quienRecoge == QuienRecogeRecogida.REPARTO)
            {
                string errorRuta = ValidarRutaPropia(cliente, contacto, ficha);
                if (errorRuta != null)
                {
                    return Mal<RecogidaSolicitadaDTO>(HttpStatusCode.BadRequest, errorRuta);
                }
            }
            string factura = string.IsNullOrWhiteSpace(peticion.FacturaOriginal) ? null : peticion.FacturaOriginal.Trim().ToUpperInvariant();
            if (factura != null)
            {
                string clienteFactura = factura.Length > 10 ? null : await repositorio.LeerClienteFactura(empresa, factura).ConfigureAwait(false);
                if (clienteFactura == null)
                {
                    return Mal<RecogidaSolicitadaDTO>(HttpStatusCode.BadRequest, $"La factura {factura} no existe. Revise el número o déjelo en blanco.");
                }
                if (!string.Equals(clienteFactura.Trim(), cliente, StringComparison.OrdinalIgnoreCase))
                {
                    return Mal<RecogidaSolicitadaDTO>(HttpStatusCode.BadRequest,
                        $"La factura {factura} es del cliente {clienteFactura.Trim()}, no del {cliente}.");
                }
            }
            List<int> abiertas = await repositorio.LeerAbiertas(empresa, cliente, contacto).ConfigureAwait(false) ?? new List<int>();
            if (abiertas.Count > 0)
            {
                return Mal<RecogidaSolicitadaDTO>(HttpStatusCode.Conflict,
                    $"El cliente {cliente}/{contacto} ya tiene la recogida {abiertas[0]} pedida y sin contestar: espere a que el almacén " +
                    "la confirme o anúlela antes de pedir otra.");
            }

            var fila = new FilaRecogidaSolicitada
            {
                Empresa = empresa,
                Cliente = cliente,
                Contacto = contacto,
                Texto = texto,
                Bultos = bultos,
                FacturaOriginal = factura,
                QuienRecoge = quienRecoge,
                Vendedor = Recortar(usuario?.Vendedor?.Trim(), 3),
                UsuarioSolicita = Recortar(usuario?.Nombre ?? "NestoAPI", 50),
                FechaSolicitud = ahora(),
                Estado = EstadosRecogidaSolicitada.SOLICITADA
            };
            fila.Id = await repositorio.Insertar(fila).ConfigureAwait(false);
            RecogidaSolicitadaDTO dto = ADto(fila, ficha, null);
            Avisar(() => avisos?.Solicitada(dto, fila.UsuarioSolicita));
            return ResultadoRecogidaSolicitada<RecogidaSolicitadaDTO>.Bien(dto, HttpStatusCode.Created);
        }

        // ================================================================ leer

        public async Task<ResultadoRecogidaSolicitada<List<RecogidaSolicitadaDTO>>> LeerDelCliente(string empresa, string cliente, string contacto)
        {
            if (!repositorio.ScriptAplicado())
            {
                return Mal<List<RecogidaSolicitadaDTO>>(HttpStatusCode.ServiceUnavailable, MENSAJE_FALTA_SCRIPT);
            }
            if (string.IsNullOrWhiteSpace(cliente))
            {
                return Mal<List<RecogidaSolicitadaDTO>>(HttpStatusCode.BadRequest, "Indique el cliente.");
            }
            string emp = Empresa(empresa);
            List<FilaRecogidaSolicitada> filas = await repositorio.LeerDelCliente(emp, cliente.Trim(),
                string.IsNullOrWhiteSpace(contacto) ? null : contacto.Trim()).ConfigureAwait(false) ?? new List<FilaRecogidaSolicitada>();
            return ResultadoRecogidaSolicitada<List<RecogidaSolicitadaDTO>>.Bien(await Construir(emp, filas).ConfigureAwait(false));
        }

        public async Task<ResultadoRecogidaSolicitada<List<RecogidaSolicitadaDTO>>> LeerPendientes(string empresa)
        {
            if (!repositorio.ScriptAplicado())
            {
                return Mal<List<RecogidaSolicitadaDTO>>(HttpStatusCode.ServiceUnavailable, MENSAJE_FALTA_SCRIPT);
            }
            string emp = Empresa(empresa);
            List<FilaRecogidaSolicitada> filas = await repositorio.LeerPendientes(emp).ConfigureAwait(false) ?? new List<FilaRecogidaSolicitada>();
            return ResultadoRecogidaSolicitada<List<RecogidaSolicitadaDTO>>.Bien(await Construir(emp, filas).ConfigureAwait(false));
        }

        /// <summary>Una solicitud con su estado calculado. 404 si no existe; 503 sin el script.</summary>
        public async Task<ResultadoRecogidaSolicitada<RecogidaSolicitadaDTO>> Leer(int id)
        {
            if (!repositorio.ScriptAplicado())
            {
                return Mal<RecogidaSolicitadaDTO>(HttpStatusCode.ServiceUnavailable, MENSAJE_FALTA_SCRIPT);
            }
            FilaRecogidaSolicitada fila = await repositorio.Leer(id).ConfigureAwait(false);
            if (fila == null)
            {
                return Mal<RecogidaSolicitadaDTO>(HttpStatusCode.NotFound, $"No existe la recogida solicitada {id}.");
            }
            List<RecogidaSolicitadaDTO> lista = await Construir(fila.Empresa, new List<FilaRecogidaSolicitada> { fila }).ConfigureAwait(false);
            return ResultadoRecogidaSolicitada<RecogidaSolicitadaDTO>.Bien(lista[0]);
        }

        // ================================================================ confirmar y rechazar (almacén)

        public async Task<ResultadoRecogidaSolicitada<RecogidaSolicitadaDTO>> Confirmar(int id, ConfirmarRecogidaSolicitadaDTO peticion, string usuario)
        {
            if (!repositorio.ScriptAplicado())
            {
                return Mal<RecogidaSolicitadaDTO>(HttpStatusCode.ServiceUnavailable, MENSAJE_FALTA_SCRIPT);
            }
            FilaRecogidaSolicitada fila = await repositorio.Leer(id).ConfigureAwait(false);
            if (fila == null)
            {
                return Mal<RecogidaSolicitadaDTO>(HttpStatusCode.NotFound, $"No existe la recogida solicitada {id}.");
            }
            if (fila.Estado != EstadosRecogidaSolicitada.SOLICITADA)
            {
                return Mal<RecogidaSolicitadaDTO>(HttpStatusCode.Conflict, YaResuelta(fila));
            }
            string texto = string.IsNullOrWhiteSpace(peticion?.Texto) ? fila.Texto : peticion.Texto.Trim();
            string errorTexto = ValidarTexto(texto);
            if (errorTexto != null)
            {
                return Mal<RecogidaSolicitadaDTO>(HttpStatusCode.BadRequest, errorTexto);
            }
            short bultos = peticion?.Bultos ?? fila.Bultos;
            string errorBultos = ValidarBultos(bultos);
            if (errorBultos != null)
            {
                return Mal<RecogidaSolicitadaDTO>(HttpStatusCode.BadRequest, errorBultos);
            }
            FichaNotaCobro ficha = await repositorio.LeerFicha(fila.Empresa, fila.Cliente, fila.Contacto).ConfigureAwait(false);
            if (ficha == null)
            {
                return Mal<RecogidaSolicitadaDTO>(HttpStatusCode.BadRequest,
                    $"No se encuentra la ficha del cliente {fila.Cliente}/{fila.Contacto}: no se puede crear la nota de recogida.");
            }
            if (fila.QuienRecoge != QuienRecogeRecogida.VENDEDOR)
            {
                string errorRuta = ValidarRutaPropia(fila.Cliente, fila.Contacto, ficha);
                if (errorRuta != null)
                {
                    return Mal<RecogidaSolicitadaDTO>(HttpStatusCode.BadRequest, errorRuta + " Rechácela con ese motivo.");
                }
            }

            int? pedido = await repositorio.Confirmar(fila, bultos, texto, LineasTexto(texto), ficha, usuario).ConfigureAwait(false);
            if (!pedido.HasValue)
            {
                FilaRecogidaSolicitada releida = await repositorio.Leer(id).ConfigureAwait(false);
                return Mal<RecogidaSolicitadaDTO>(HttpStatusCode.Conflict, releida == null ? $"No existe la recogida solicitada {id}." : YaResuelta(releida));
            }
            fila.Estado = EstadosRecogidaSolicitada.CONFIRMADA;
            fila.PedidoNota = pedido;
            fila.Bultos = bultos;
            fila.Texto = texto;
            fila.UsuarioResuelve = Recortar(usuario, 50);
            fila.FechaResolucion = ahora();
            RecogidaSolicitadaDTO dto = ADto(fila, ficha, new SeguimientoNotaRecogida { Existe = true });
            Avisar(() => avisos?.Resuelta(dto, usuario));
            return ResultadoRecogidaSolicitada<RecogidaSolicitadaDTO>.Bien(dto);
        }

        public async Task<ResultadoRecogidaSolicitada<RecogidaSolicitadaDTO>> Rechazar(int id, RechazarRecogidaSolicitadaDTO peticion, string usuario)
        {
            if (!repositorio.ScriptAplicado())
            {
                return Mal<RecogidaSolicitadaDTO>(HttpStatusCode.ServiceUnavailable, MENSAJE_FALTA_SCRIPT);
            }
            string motivo = peticion?.Motivo?.Trim();
            if (string.IsNullOrEmpty(motivo))
            {
                return Mal<RecogidaSolicitadaDTO>(HttpStatusCode.BadRequest, "Indique el motivo del rechazo: se lo enseñamos a quien la pidió.");
            }
            if (motivo.Length > MAXIMO_MOTIVO)
            {
                return Mal<RecogidaSolicitadaDTO>(HttpStatusCode.BadRequest, $"El motivo no puede pasar de {MAXIMO_MOTIVO} caracteres.");
            }
            FilaRecogidaSolicitada fila = await repositorio.Leer(id).ConfigureAwait(false);
            if (fila == null)
            {
                return Mal<RecogidaSolicitadaDTO>(HttpStatusCode.NotFound, $"No existe la recogida solicitada {id}.");
            }
            if (fila.Estado != EstadosRecogidaSolicitada.SOLICITADA || !await repositorio.Rechazar(id, motivo, usuario).ConfigureAwait(false))
            {
                FilaRecogidaSolicitada releida = await repositorio.Leer(id).ConfigureAwait(false) ?? fila;
                return Mal<RecogidaSolicitadaDTO>(HttpStatusCode.Conflict, YaResuelta(releida));
            }
            fila.Estado = EstadosRecogidaSolicitada.RECHAZADA;
            fila.MotivoRechazo = motivo;
            fila.UsuarioResuelve = Recortar(usuario, 50);
            fila.FechaResolucion = ahora();
            FichaNotaCobro ficha = await repositorio.LeerFicha(fila.Empresa, fila.Cliente, fila.Contacto).ConfigureAwait(false);
            RecogidaSolicitadaDTO dto = ADto(fila, ficha, null);
            Avisar(() => avisos?.Resuelta(dto, usuario));
            return ResultadoRecogidaSolicitada<RecogidaSolicitadaDTO>.Bien(dto);
        }

        // ================================================================ anular

        public async Task<ResultadoRecogidaSolicitada<RecogidaSolicitadaDTO>> Anular(int id, string motivo, UsuarioRecogidas usuario)
        {
            if (!repositorio.ScriptAplicado())
            {
                return Mal<RecogidaSolicitadaDTO>(HttpStatusCode.ServiceUnavailable, MENSAJE_FALTA_SCRIPT);
            }
            motivo = string.IsNullOrWhiteSpace(motivo) ? null : motivo.Trim();
            if (motivo != null && motivo.Length > MAXIMO_MOTIVO)
            {
                return Mal<RecogidaSolicitadaDTO>(HttpStatusCode.BadRequest, $"El motivo no puede pasar de {MAXIMO_MOTIVO} caracteres.");
            }
            FilaRecogidaSolicitada fila = await repositorio.Leer(id).ConfigureAwait(false);
            if (fila == null)
            {
                return Mal<RecogidaSolicitadaDTO>(HttpStatusCode.NotFound, $"No existe la recogida solicitada {id}.");
            }
            usuario = usuario ?? new UsuarioRecogidas();
            bool esQuienLaPidio = MismoUsuario(fila.UsuarioSolicita, usuario.Nombre);
            if (fila.Estado == EstadosRecogidaSolicitada.RECHAZADA || fila.Estado == EstadosRecogidaSolicitada.ANULADA)
            {
                return Mal<RecogidaSolicitadaDTO>(HttpStatusCode.Conflict, YaResuelta(fila));
            }
            string nombre = Recortar(usuario.Nombre ?? "NestoAPI", 50);
            FichaNotaCobro ficha = await repositorio.LeerFicha(fila.Empresa, fila.Cliente, fila.Contacto).ConfigureAwait(false);

            if (fila.Estado == EstadosRecogidaSolicitada.SOLICITADA)
            {
                if (!esQuienLaPidio && !usuario.PuedeGestionar)
                {
                    return Mal<RecogidaSolicitadaDTO>(HttpStatusCode.Forbidden, "Solo puede anular una recogida quien la pidió (o el almacén).");
                }
                if (!await repositorio.Anular(id, EstadosRecogidaSolicitada.SOLICITADA, motivo, nombre).ConfigureAwait(false))
                {
                    FilaRecogidaSolicitada releida = await repositorio.Leer(id).ConfigureAwait(false) ?? fila;
                    return Mal<RecogidaSolicitadaDTO>(HttpStatusCode.Conflict, YaResuelta(releida) + " Vuelva a consultarla.");
                }
                return Anulada(fila, ficha, null, motivo, nombre, avisarAlmacen: false, aviso: null);
            }

            // Confirmada: la nota existe (o existía)
            SeguimientoNotaRecogida seguimiento = fila.PedidoNota.HasValue
                ? await repositorio.LeerSeguimiento(fila.Empresa, fila.PedidoNota.Value).ConfigureAwait(false) ?? new SeguimientoNotaRecogida()
                : new SeguimientoNotaRecogida();
            string estado = EstadoCalculado(fila, seguimiento);
            if (estado == EstadosRecogidaSolicitada.RECOGIDA)
            {
                return Mal<RecogidaSolicitadaDTO>(HttpStatusCode.Conflict,
                    $"La recogida {id} ya está recogida{(fila.PedidoNota.HasValue ? $" (nota {CodigoNotaEntrega.Documento(true, fila.PedidoNota.Value)})" : string.Empty)}: ya no se puede anular.");
            }
            bool sinEmpezar = !seguimiento.Existe || (!seguimiento.Empezada && !seguimiento.TieneEnvio && !seguimiento.PedidoAbono.HasValue);
            if (sinEmpezar)
            {
                if (!esQuienLaPidio && !usuario.PuedeGestionar)
                {
                    return Mal<RecogidaSolicitadaDTO>(HttpStatusCode.Forbidden, "Solo puede anular una recogida quien la pidió (o el almacén).");
                }
                bool hecho = seguimiento.Existe
                    ? await repositorio.AnularBorrandoNota(id, fila.Empresa, fila.PedidoNota.Value, motivo, nombre).ConfigureAwait(false)
                    : await repositorio.Anular(id, EstadosRecogidaSolicitada.CONFIRMADA, motivo, nombre).ConfigureAwait(false);
                if (hecho)
                {
                    return Anulada(fila, ficha, seguimiento, motivo, nombre, avisarAlmacen: false, aviso: null);
                }
                FilaRecogidaSolicitada releida = await repositorio.Leer(id).ConfigureAwait(false) ?? fila;
                if (!seguimiento.Existe || releida.Estado != EstadosRecogidaSolicitada.CONFIRMADA)
                {
                    return Mal<RecogidaSolicitadaDTO>(HttpStatusCode.Conflict, YaResuelta(releida) + " Vuelva a consultarla.");
                }
                // La nota ha empezado (picking, parada) entre medias: sigue por la anulación en reparto.
                seguimiento = await repositorio.LeerSeguimiento(fila.Empresa, fila.PedidoNota.Value).ConfigureAwait(false) ?? new SeguimientoNotaRecogida { Existe = true, Empezada = true };
            }

            // Ya en reparto (parada o picking): solo Almacén o Administración, y la nota se queda (se avisa a Almacén).
            if (!usuario.PuedeAnularEnReparto)
            {
                return Mal<RecogidaSolicitadaDTO>(HttpStatusCode.Forbidden,
                    $"La recogida {id} ya está en marcha en el almacén (nota {CodigoNotaEntrega.Documento(true, fila.PedidoNota.Value)}): " +
                    "solo Almacén o Administración pueden anularla. Llame al almacén.");
            }
            if (!await repositorio.Anular(id, EstadosRecogidaSolicitada.CONFIRMADA, motivo, nombre).ConfigureAwait(false))
            {
                FilaRecogidaSolicitada releida = await repositorio.Leer(id).ConfigureAwait(false) ?? fila;
                return Mal<RecogidaSolicitadaDTO>(HttpStatusCode.Conflict, YaResuelta(releida) + " Vuelva a consultarla.");
            }
            string codigo = CodigoNotaEntrega.Documento(true, fila.PedidoNota.Value);
            string avisoAnulacion = $"La nota de recogida {codigo} sigue viva: quite su parada del reparto (o avise al repartidor para que no la " +
                "recoja) y borre la nota desde Nesto si no se va a hacer.";
            return Anulada(fila, ficha, seguimiento, motivo, nombre, avisarAlmacen: true, aviso: avisoAnulacion);
        }

        private ResultadoRecogidaSolicitada<RecogidaSolicitadaDTO> Anulada(FilaRecogidaSolicitada fila, FichaNotaCobro ficha,
            SeguimientoNotaRecogida seguimiento, string motivo, string usuario, bool avisarAlmacen, string aviso)
        {
            fila.Estado = EstadosRecogidaSolicitada.ANULADA;
            fila.MotivoAnulacion = motivo;
            fila.UsuarioAnula = usuario;
            fila.FechaAnulacion = ahora();
            RecogidaSolicitadaDTO dto = ADto(fila, ficha, seguimiento);
            dto.AvisoAnulacion = aviso;
            Avisar(() => avisos?.Anulada(dto, usuario, avisarAlmacen));
            return ResultadoRecogidaSolicitada<RecogidaSolicitadaDTO>.Bien(dto);
        }

        // ================================================================ un día laborable sin respuesta (job)

        /// <summary>A cuántas solicitudes se ha avisado. Sin el script, ninguna. Nunca lanza.</summary>
        public async Task<int> AvisarSinRespuesta()
        {
            try
            {
                if (!repositorio.ScriptAplicado())
                {
                    return 0;
                }
                DateTime momento = ahora();
                List<FilaRecogidaSolicitada> filas = await repositorio.LeerSinAvisar().ConfigureAwait(false) ?? new List<FilaRecogidaSolicitada>();
                int avisadas = 0;
                foreach (FilaRecogidaSolicitada fila in filas.Where(f => f.Estado == EstadosRecogidaSolicitada.SOLICITADA && !f.AvisoSinRespuesta.HasValue))
                {
                    if (momento < VenceSinRespuesta(fila.FechaSolicitud, esFestivo))
                    {
                        continue;
                    }
                    if (!await repositorio.MarcarAvisoSinRespuesta(fila.Id).ConfigureAwait(false))
                    {
                        continue; // ya avisada (otro servidor) o contestada entre medias
                    }
                    fila.AvisoSinRespuesta = momento;
                    FichaNotaCobro ficha = await repositorio.LeerFicha(fila.Empresa, fila.Cliente, fila.Contacto).ConfigureAwait(false);
                    if (avisos != null)
                    {
                        await avisos.SinRespuesta(ADto(fila, ficha, null)).ConfigureAwait(false);
                    }
                    avisadas++;
                }
                return avisadas;
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Recogidas solicitadas #855] No se ha podido avisar de las recogidas sin respuesta: {ex.Message}", ex));
                return 0;
            }
        }

        /// <summary>
        /// Cuándo una solicitud lleva UN día laborable (Algete) sin respuesta: el siguiente laborable después del día en que se pidió, a la
        /// misma hora (pedida el viernes a las 10:00 → el lunes a las 10:00). Pedida en un día que no es laborable, cuenta desde el
        /// principio del primer laborable (pedida el sábado → vence al acabar el lunes, el martes a las 0:00).
        /// </summary>
        public static DateTime VenceSinRespuesta(DateTime fechaSolicitud, Func<DateTime, bool> esFestivo)
        {
            if (esFestivo == null)
            {
                throw new ArgumentNullException(nameof(esFestivo));
            }
            DateTime dia = fechaSolicitud.Date;
            TimeSpan hora = fechaSolicitud.TimeOfDay;
            if (!EsLaborable(dia, esFestivo))
            {
                dia = SiguienteLaborable(dia, esFestivo);
                hora = TimeSpan.Zero;
            }
            return SiguienteLaborable(dia, esFestivo).Add(hora);
        }

        private static bool EsLaborable(DateTime dia, Func<DateTime, bool> esFestivo)
            => dia.DayOfWeek != DayOfWeek.Saturday && dia.DayOfWeek != DayOfWeek.Sunday && !esFestivo(dia);

        private static DateTime SiguienteLaborable(DateTime dia, Func<DateTime, bool> esFestivo)
        {
            DateTime siguiente = dia.Date.AddDays(1);
            for (int i = 0; i < 60 && !EsLaborable(siguiente, esFestivo); i++)
            {
                siguiente = siguiente.AddDays(1);
            }
            return siguiente;
        }

        // ================================================================ reglas puras

        /// <summary>
        /// El estado para el cliente de la API: los guardados tal cual y, con nota, Recogida (abono hecho, parada entregada o bultos NR
        /// recogidos), EnReparto (parada tramitada o incidentada) o Confirmada.
        /// </summary>
        public static string EstadoCalculado(FilaRecogidaSolicitada fila, SeguimientoNotaRecogida seguimiento)
        {
            if (fila == null)
            {
                return null;
            }
            if (fila.Estado != EstadosRecogidaSolicitada.CONFIRMADA || seguimiento == null)
            {
                return fila.Estado;
            }
            if (seguimiento.PedidoAbono.HasValue || seguimiento.EstadoParada == Constantes.Agencias.ESTADO_ENTREGADO || seguimiento.BultosRecogidos)
            {
                return EstadosRecogidaSolicitada.RECOGIDA;
            }
            if (seguimiento.EstadoParada == Constantes.Agencias.ESTADO_TRAMITADO || seguimiento.EstadoParada == Constantes.Agencias.ESTADO_INCIDENTADO)
            {
                return EstadosRecogidaSolicitada.EN_REPARTO;
            }
            return EstadosRecogidaSolicitada.CONFIRMADA;
        }

        /// <summary>El texto en líneas de texto de la nota (LinPedidoVta.Texto es de 50): por palabras, respetando los saltos de línea.</summary>
        public static List<string> LineasTexto(string texto)
        {
            var lineas = new List<string>();
            foreach (string parrafo in (texto ?? string.Empty).Replace("\r", string.Empty).Split('\n'))
            {
                string actual = string.Empty;
                foreach (string palabra in parrafo.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string resto = palabra;
                    while (resto.Length > LONGITUD_LINEA_TEXTO)
                    {
                        if (actual.Length > 0)
                        {
                            lineas.Add(actual);
                            actual = string.Empty;
                        }
                        lineas.Add(resto.Substring(0, LONGITUD_LINEA_TEXTO));
                        resto = resto.Substring(LONGITUD_LINEA_TEXTO);
                    }
                    if (actual.Length == 0)
                    {
                        actual = resto;
                    }
                    else if (actual.Length + 1 + resto.Length <= LONGITUD_LINEA_TEXTO)
                    {
                        actual += " " + resto;
                    }
                    else
                    {
                        lineas.Add(actual);
                        actual = resto;
                    }
                }
                if (actual.Length > 0)
                {
                    lineas.Add(actual);
                }
            }
            return lineas;
        }

        /// <summary>«NR930123-1» … «NR930123-{bultos}».</summary>
        public static List<string> CodigosBultos(int pedido, short bultos)
            => Enumerable.Range(1, Math.Max(1, (int)bultos)).Select(n => CodigoNotaEntrega.BultoRecogida(pedido, n)).ToList();

        /// <summary>El comentario de la cabecera de la nota (lo que ve el almacén en el pedido).</summary>
        public static string ComentarioNota(FilaRecogidaSolicitada fila, string texto)
        {
            string quien = string.IsNullOrWhiteSpace(fila?.UsuarioSolicita) ? string.Empty : $" por {fila.UsuarioSolicita.Trim()}";
            string comentario = $"Nota de recogida solicitada{quien} (solicitud {fila?.Id}, NestoAPI#855): {texto}";
            if (fila?.QuienRecoge == QuienRecogeRecogida.VENDEDOR)
            {
                comentario += "\r\nLA RECOGE EL VENDEDOR: no va en el reparto. Se cierra al leer en Algete el código de la nota.";
            }
            return comentario;
        }

        public static bool MismoUsuario(string a, string b)
        {
            string x = SinDominio(a);
            string y = SinDominio(b);
            return !string.IsNullOrEmpty(x) && string.Equals(x, y, StringComparison.OrdinalIgnoreCase);
        }

        internal static string SinDominio(string usuario)
        {
            string u = usuario?.Trim();
            if (string.IsNullOrEmpty(u))
            {
                return null;
            }
            int barra = u.LastIndexOf('\\');
            return barra >= 0 ? u.Substring(barra + 1) : u;
        }

        public RecogidaSolicitadaDTO ADto(FilaRecogidaSolicitada fila, FichaNotaCobro ficha, SeguimientoNotaRecogida seguimiento)
        {
            var dto = new RecogidaSolicitadaDTO
            {
                Id = fila.Id,
                Empresa = fila.Empresa?.Trim(),
                Cliente = fila.Cliente?.Trim(),
                Contacto = fila.Contacto?.Trim(),
                NombreCliente = ficha?.Nombre,
                Direccion = ficha?.Direccion,
                CodPostal = ficha?.CodPostal,
                Poblacion = ficha?.Poblacion,
                Telefono = ficha?.Telefonos,
                Ruta = ficha?.Ruta,
                Texto = fila.Texto,
                Bultos = fila.Bultos,
                FacturaOriginal = string.IsNullOrWhiteSpace(fila.FacturaOriginal) ? null : fila.FacturaOriginal.Trim(),
                QuienRecoge = QuienRecogeRecogida.Normalizar(fila.QuienRecoge) ?? QuienRecogeRecogida.REPARTO,
                Vendedor = string.IsNullOrWhiteSpace(fila.Vendedor) ? null : fila.Vendedor.Trim(),
                UsuarioSolicita = fila.UsuarioSolicita?.Trim(),
                FechaSolicitud = fila.FechaSolicitud,
                Estado = EstadoCalculado(fila, seguimiento),
                PedidoNota = fila.PedidoNota,
                UsuarioResuelve = fila.UsuarioResuelve?.Trim(),
                FechaResolucion = fila.FechaResolucion,
                MotivoRechazo = fila.MotivoRechazo,
                UsuarioAnula = fila.UsuarioAnula?.Trim(),
                FechaAnulacion = fila.FechaAnulacion,
                MotivoAnulacion = fila.MotivoAnulacion,
                FechaAvisoSinRespuesta = fila.AvisoSinRespuesta
            };
            if (fila.PedidoNota.HasValue)
            {
                dto.CodigoDocumento = CodigoNotaEntrega.Documento(true, fila.PedidoNota.Value);
                dto.CodigosBultos = CodigosBultos(fila.PedidoNota.Value, fila.Bultos);
            }
            return dto;
        }

        private async Task<List<RecogidaSolicitadaDTO>> Construir(string empresa, List<FilaRecogidaSolicitada> filas)
        {
            Dictionary<string, FichaNotaCobro> fichas = filas.Count == 0 ? new Dictionary<string, FichaNotaCobro>()
                : await repositorio.LeerFichas(empresa, filas.Select(f => f.Cliente?.Trim()).Distinct().ToList()).ConfigureAwait(false)
                  ?? new Dictionary<string, FichaNotaCobro>();
            var resultado = new List<RecogidaSolicitadaDTO>();
            foreach (FilaRecogidaSolicitada fila in filas)
            {
                SeguimientoNotaRecogida seguimiento = null;
                if (fila.Estado == EstadosRecogidaSolicitada.CONFIRMADA && fila.PedidoNota.HasValue)
                {
                    seguimiento = await repositorio.LeerSeguimiento(fila.Empresa, fila.PedidoNota.Value).ConfigureAwait(false);
                }
                _ = fichas.TryGetValue(Clave(fila.Cliente, fila.Contacto), out FichaNotaCobro ficha);
                resultado.Add(ADto(fila, ficha, seguimiento));
            }
            return resultado;
        }

        internal static string Clave(string cliente, string contacto) => $"{cliente?.Trim()}/{contacto?.Trim()}".ToUpperInvariant();

        private string ValidarRutaPropia(string cliente, string contacto, FichaNotaCobro ficha)
        {
            if (!RutasRepartoPropio.EsRutaPropia(ficha.Ruta))
            {
                return $"El reparto propio solo recoge en nuestra ruta y el cliente {cliente}/{contacto} tiene la ruta «{ficha.Ruta?.Trim()}». " +
                    $"Si pasa usted por el cliente, pídala como «la recojo yo» (QuienRecoge = {QuienRecogeRecogida.VENDEDOR}).";
            }
            if (!repartoPropioActivo())
            {
                return "El reparto propio está apagado: no puede recoger. Si pasa usted por el cliente, pídala como «la recojo yo» " +
                    $"(QuienRecoge = {QuienRecogeRecogida.VENDEDOR}).";
            }
            return null;
        }

        private static string ValidarTexto(string texto)
        {
            if (string.IsNullOrWhiteSpace(texto))
            {
                return "Indique qué hay que recoger.";
            }
            return texto.Length > MAXIMO_TEXTO ? $"Lo que hay que recoger no puede pasar de {MAXIMO_TEXTO} caracteres." : null;
        }

        private static string ValidarBultos(short bultos)
        {
            if (bultos < 1)
            {
                return "Una recogida tiene que llevar al menos un bulto.";
            }
            return bultos > TiposNotaEntrega.BULTOS_MAXIMO ? $"Una recogida no puede llevar más de {TiposNotaEntrega.BULTOS_MAXIMO} bultos." : null;
        }

        private static string YaResuelta(FilaRecogidaSolicitada fila)
        {
            switch (fila.Estado)
            {
                case EstadosRecogidaSolicitada.CONFIRMADA:
                    return $"La recogida {fila.Id} ya está confirmada" +
                        (fila.PedidoNota.HasValue ? $" (nota {CodigoNotaEntrega.Documento(true, fila.PedidoNota.Value)})." : ".");
                case EstadosRecogidaSolicitada.RECHAZADA:
                    return $"La recogida {fila.Id} ya está rechazada" + (string.IsNullOrWhiteSpace(fila.MotivoRechazo) ? "." : $": {fila.MotivoRechazo.Trim()}");
                case EstadosRecogidaSolicitada.ANULADA:
                    return $"La recogida {fila.Id} ya está anulada.";
                default:
                    return $"La recogida {fila.Id} está «{fila.Estado}».";
            }
        }

        private static string Empresa(string empresa)
            => string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();

        private static ResultadoRecogidaSolicitada<T> Mal<T>(HttpStatusCode codigo, string mensaje) => ResultadoRecogidaSolicitada<T>.Mal(codigo, mensaje);

        private static void Avisar(Action avisar)
        {
            try
            {
                avisar();
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Recogidas solicitadas #855] No se ha podido avisar: {ex.Message}", ex));
            }
        }

        internal static string Recortar(string valor, int maximo) => valor == null || valor.Length <= maximo ? valor : valor.Substring(0, maximo);

        internal static string Entero(int valor) => valor.ToString(CultureInfo.InvariantCulture);
    }
}
