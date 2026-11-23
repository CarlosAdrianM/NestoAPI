using NestoAPI.Infraestructure.Eventos;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.Vendedores;
using NestoAPI.Infraestructure.Verifactu;
using NestoAPI.Infrastructure;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Globalization;
using System.Linq;
using System.Security.Principal;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Cupones
{
    /// <summary>NestoAPI#885: POST api/Cupones/{codigo}/Adelantar.</summary>
    public class AdelantarCuponDTO
    {
        /// <summary>Obligatorio, 1..500 caracteres.</summary>
        public string Motivo { get; set; }
    }

    /// <summary>NestoAPI#885: a qué cupones puede adelantar un usuario.</summary>
    public enum AlcanceAdelantarCupon
    {
        /// <summary>A ninguno.</summary>
        Ninguno = 0,
        /// <summary>A cualquier cliente (Dirección o ParametrosUsuario «Cupones.PuedeAdelantar» = «1»).</summary>
        Todos = 1,
        /// <summary>Solo a los clientes cuyo vendedor es de su equipo («Cupones.PuedeAdelantar» = «Equipo»).</summary>
        Equipo = 2
    }

    /// <summary>
    /// NestoAPI#885 (corte 1, 23/11/26): LA REGLA de adelantar un cupón de evento por excepción. Un cupón de evento vale desde el día del
    /// evento (Emitido hasta entonces); por excepción, y por cupón, se puede hacer valer desde hoy. Pura: sin base de datos.
    /// </summary>
    public static class ReglaAdelantarCupon
    {
        /// <summary>ParametrosUsuario (empresa 1, fila PROPIA del usuario; no hay «(defecto)»): «1» = cualquier cliente, «Equipo» = los de su equipo.</summary>
        public const string CLAVE_PARAMETRO = "Cupones.PuedeAdelantar";
        public const string VALOR_TODOS = "1";
        public const string VALOR_EQUIPO = "Equipo";
        public const int LONGITUD_MOTIVO = 500;
        /// <summary>Tabla del rastro en Modificaciones.</summary>
        public const string TABLA_MODIFICACIONES = "CuponAdelantado";
        public const string ERROR_NO_SE_PUEDE_ADELANTAR = "CUPON_NO_SE_PUEDE_ADELANTAR";
        public const string TIPO_NOTIFICACION = "CuponAdelantado";

        public const string MENSAJE_SIN_PERMISO =
            "No tiene permiso para adelantar este cupón. Pídaselo a Dirección.";
        public const string MENSAJE_CLIENTE_TIENDA = "Los cupones solo los puede adelantar la casa.";
        public const string MENSAJE_SIN_MOTIVO = "Indique el motivo por el que se adelanta el cupón: es obligatorio.";

        private static readonly CultureInfo es = CultureInfo.GetCultureInfo("es-ES");

        /// <summary>
        /// El alcance del usuario. Un cliente de la tienda (o nadie), ninguno; Dirección, todos (sin mirar el parámetro); si no, el
        /// valor de su parámetro «Cupones.PuedeAdelantar» («1» o «Equipo»; cualquier otra cosa o sin fila, ninguno).
        /// </summary>
        public static AlcanceAdelantarCupon Alcance(bool esClienteTienda, bool esDireccion, string valorParametro)
        {
            if (esClienteTienda)
            {
                return AlcanceAdelantarCupon.Ninguno;
            }
            if (esDireccion)
            {
                return AlcanceAdelantarCupon.Todos;
            }
            string valor = valorParametro?.Trim();
            if (valor == VALOR_TODOS)
            {
                return AlcanceAdelantarCupon.Todos;
            }
            return string.Equals(valor, VALOR_EQUIPO, StringComparison.OrdinalIgnoreCase) ? AlcanceAdelantarCupon.Equipo : AlcanceAdelantarCupon.Ninguno;
        }

        /// <summary>El parámetro solo se lee si hace falta: ni para Dirección ni para un cliente de la tienda.</summary>
        public static bool HayQueLeerParametro(bool esClienteTienda, bool esDireccion) => !esClienteTienda && !esDireccion;

        /// <summary>
        /// ¿Puede con el cupón de un cliente cuyo vendedor es <paramref name="vendedorDelCliente"/>? Con «Equipo», solo si ese vendedor es
        /// de su equipo (<paramref name="equipo"/>: él y, si es jefe, los suyos).
        /// </summary>
        public static bool Puede(AlcanceAdelantarCupon alcance, string vendedorDelCliente, IEnumerable<string> equipo)
        {
            if (alcance == AlcanceAdelantarCupon.Todos)
            {
                return true;
            }
            if (alcance != AlcanceAdelantarCupon.Equipo || string.IsNullOrWhiteSpace(vendedorDelCliente))
            {
                return false;
            }
            return (equipo ?? Enumerable.Empty<string>())
                .Any(v => string.Equals(v?.Trim(), vendedorDelCliente.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>De evento y Emitido hoy (todavía no vale).</summary>
        public static bool EsAdelantable(CuponLeido cupon, DateTime hoy) => cupon != null && MotivoNoAdelantable(cupon, hoy) == null;

        /// <summary>Por qué no se puede adelantar (de usted, para el 409), o null si se puede.</summary>
        public static string MotivoNoAdelantable(CuponLeido cupon, DateTime hoy)
        {
            if (cupon == null)
            {
                return "No existe el cupón.";
            }
            string codigo = cupon.Codigo?.Trim();
            if (!string.Equals(cupon.Origen?.Trim(), OrigenesCupon.EVENTO, StringComparison.OrdinalIgnoreCase))
            {
                return $"El cupón {codigo} no es de un evento: solo se pueden adelantar los cupones de los eventos.";
            }
            switch (ReglasCupon.EstadoEfectivo(cupon, hoy))
            {
                case EstadosCupon.EMITIDO:
                    return null;
                case EstadosCupon.ACTIVO:
                    return $"El cupón {codigo} ya se puede usar desde el {ReglasCupon.Fecha(cupon.ValidoDesde)}: no hace falta adelantarlo.";
                case EstadosCupon.USADO:
                    return $"El cupón {codigo} {ReglasCupon.DondeEstaUsado(cupon)}: no se puede adelantar.";
                case EstadosCupon.ANULADO:
                    return $"El cupón {codigo} está anulado: no se puede adelantar.";
                case EstadosCupon.CADUCADO:
                    return $"El cupón {codigo} caducó el {ReglasCupon.Fecha(cupon.Caducidad)}: no se puede adelantar.";
                default:
                    return $"El cupón {codigo} no se puede adelantar.";
            }
        }

        /// <summary>El motivo de la petición: obligatorio y de 500 caracteres como mucho. Null si vale.</summary>
        public static string MotivoNoValido(string motivo)
        {
            string limpio = motivo?.Trim();
            if (string.IsNullOrEmpty(limpio))
            {
                return MENSAJE_SIN_MOTIVO;
            }
            return limpio.Length > LONGITUD_MOTIVO ? $"El motivo no puede tener más de {LONGITUD_MOTIVO} caracteres." : null;
        }

        /// <summary>Lo de antes para Modificaciones.Anterior (JSON).</summary>
        public static string RastroAnterior(CuponLeido cupon) =>
            Newtonsoft.Json.JsonConvert.SerializeObject(new
            {
                Codigo = cupon.Codigo?.Trim(),
                Empresa = cupon.Empresa?.Trim(),
                Cliente = cupon.Cliente?.Trim(),
                Contacto = cupon.Contacto?.Trim(),
                cupon.Importe,
                ValidoDesde = FechaIso(cupon.ValidoDesde)
            });

        /// <summary>Lo de después para Modificaciones.Nuevo (JSON): con la fecha de antes y la nueva, quién y por qué.</summary>
        public static string RastroNuevo(CuponLeido cupon, DateTime validoDesdeNuevo, string usuario, string motivo) =>
            Newtonsoft.Json.JsonConvert.SerializeObject(new
            {
                Codigo = cupon.Codigo?.Trim(),
                Empresa = cupon.Empresa?.Trim(),
                Cliente = cupon.Cliente?.Trim(),
                Contacto = cupon.Contacto?.Trim(),
                cupon.Importe,
                ValidoDesdeAnterior = FechaIso(cupon.ValidoDesde),
                ValidoDesde = FechaIso(validoDesdeNuevo),
                Usuario = usuario,
                Motivo = motivo
            });

        /// <summary>La campana a Administración: «Cupón EV-… de 100,00 € del cliente 40513 adelantado por Manuel: motivo».</summary>
        public static NotificacionPushDTO Notificacion(CuponLeido cupon, DateTime validoDesdeAnterior, string usuario, string motivo)
        {
            string quien = PermisosEventos.SinDominio(usuario) ?? "NestoAPI";
            return new NotificacionPushDTO
            {
                Titulo = $"Cupón {cupon.Codigo?.Trim()} adelantado",
                Cuerpo = $"Cupón {cupon.Codigo?.Trim()} de {ReglasCupon.Euros(cupon.Importe)} € del cliente {cupon.Cliente?.Trim()} adelantado por {quien}: " +
                    $"{motivo?.Trim()} (valía desde el {ReglasCupon.Fecha(validoDesdeAnterior)}; ya se puede usar).",
                Tipo = TIPO_NOTIFICACION,
                Datos = new Dictionary<string, string>
                {
                    ["tipo"] = TIPO_NOTIFICACION,
                    ["empresa"] = cupon.Empresa?.Trim(),
                    ["cliente"] = cupon.Cliente?.Trim(),
                    ["contacto"] = cupon.Contacto?.Trim(),
                    ["codigo"] = cupon.Codigo?.Trim(),
                    ["importe"] = cupon.Importe.ToString(CultureInfo.InvariantCulture)
                }
            };
        }

        private static string FechaIso(DateTime fecha) => fecha.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    /// <summary>NestoAPI#885: los datos que hacen falta para el permiso (sustituible en los tests).</summary>
    public interface ILectorPermisoAdelantarCupon
    {
        /// <summary>
        /// ParametrosUsuario (empresa 1) «Cupones.PuedeAdelantar» de la fila PROPIA del usuario (sin dominio). Null si no la tiene. NO crea
        /// la fila ni mira «(defecto)».
        /// </summary>
        Task<string> LeerParametro(string usuarioSinDominio);

        /// <summary>El vendedor del usuario: el claim «Vendedor» o su parámetro de usuario «Vendedor». Null si no tiene.</summary>
        string VendedorDelUsuario(IPrincipal usuario);

        /// <summary>Su equipo (<see cref="IServicioVendedores.VendedoresEquipoString"/>).</summary>
        Task<List<string>> VendedoresDelEquipo(string empresa, string vendedor);

        /// <summary>
        /// Clientes.Vendedor de cada cliente/contacto (si el contacto no viene o no existe, el del contacto principal), en UNA consulta.
        /// Clave <see cref="LectorVendedoresInscripcion.ClaveClienteContacto"/>.
        /// </summary>
        Task<Dictionary<string, string>> VendedoresDeLosClientes(string empresa, IEnumerable<KeyValuePair<string, string>> clientesYContactos);
    }

    public class LectorPermisoAdelantarCupon : ILectorPermisoAdelantarCupon
    {
        private readonly NVEntities db;
        private readonly LectorVendedoresInscripcion vendedores;

        public LectorPermisoAdelantarCupon(NVEntities db, IServicioVendedores servicioVendedores = null)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
            vendedores = new LectorVendedoresInscripcion(db, servicioVendedores);
        }

        public async Task<string> LeerParametro(string usuarioSinDominio)
        {
            if (string.IsNullOrWhiteSpace(usuarioSinDominio))
            {
                return null;
            }
            string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO;
            string usuario = usuarioSinDominio.Trim();
            string valor = await db.ParametrosUsuario
                .Where(p => p.Empresa == empresa && p.Clave == ReglaAdelantarCupon.CLAVE_PARAMETRO && p.Usuario == usuario)
                .Select(p => p.Valor)
                .FirstOrDefaultAsync().ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
        }

        public string VendedorDelUsuario(IPrincipal usuario) => vendedores.VendedorDelUsuario(usuario);

        public Task<List<string>> VendedoresDelEquipo(string empresa, string vendedor) => vendedores.VendedoresDelEquipo(empresa, vendedor);

        public async Task<Dictionary<string, string>> VendedoresDeLosClientes(string empresa, IEnumerable<KeyValuePair<string, string>> clientesYContactos)
        {
            var pedidos = (clientesYContactos ?? Enumerable.Empty<KeyValuePair<string, string>>())
                .Where(p => !string.IsNullOrWhiteSpace(p.Key))
                .Select(p => new KeyValuePair<string, string>(p.Key.Trim(), string.IsNullOrWhiteSpace(p.Value) ? null : p.Value.Trim()))
                .ToList();
            var resultado = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!pedidos.Any())
            {
                return resultado;
            }
            List<string> clientes = pedidos.Select(p => p.Key).Distinct().ToList();
            var fichas = (await db.Clientes
                .Where(c => c.Empresa == empresa && clientes.Contains(c.Nº_Cliente))
                .Select(c => new { c.Nº_Cliente, c.Contacto, c.ClientePrincipal, c.Vendedor })
                .ToListAsync().ConfigureAwait(false))
                .ToLookup(c => c.Nº_Cliente?.Trim(), StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, string> pedido in pedidos)
            {
                var delCliente = fichas[pedido.Key].ToList();
                var ficha = (pedido.Value == null ? null : delCliente.FirstOrDefault(f => f.Contacto?.Trim() == pedido.Value))
                    ?? delCliente.FirstOrDefault(f => f.ClientePrincipal);
                string vendedor = ficha?.Vendedor?.Trim();
                resultado[LectorVendedoresInscripcion.ClaveClienteContacto(pedido.Key, pedido.Value)] = string.IsNullOrEmpty(vendedor) ? null : vendedor;
            }
            return resultado;
        }
    }

    /// <summary>NestoAPI#885: junta los datos y aplica <see cref="ReglaAdelantarCupon"/>.</summary>
    public interface IAutorizadorAdelantarCupon
    {
        /// <summary>Los Id de <paramref name="cupones"/> que el usuario puede adelantar (sin mirar si el cupón es adelantable). Una lectura del
        /// parámetro por petición y, con «Equipo», una del equipo y una de los vendedores de los clientes por empresa.</summary>
        Task<HashSet<int>> QuePuedeAdelantar(IPrincipal usuario, IEnumerable<CuponLeido> cupones);
    }

    public class AutorizadorAdelantarCupon : IAutorizadorAdelantarCupon
    {
        private readonly ILectorPermisoAdelantarCupon lector;

        /// <param name="lector">Null (tests que no lo miran): solo Dirección; no se lee nada.</param>
        public AutorizadorAdelantarCupon(ILectorPermisoAdelantarCupon lector)
        {
            this.lector = lector;
        }

        public async Task<HashSet<int>> QuePuedeAdelantar(IPrincipal usuario, IEnumerable<CuponLeido> cupones)
        {
            var resultado = new HashSet<int>();
            List<CuponLeido> lista = (cupones ?? Enumerable.Empty<CuponLeido>()).Where(c => c != null).ToList();
            if (!lista.Any() || usuario?.Identity?.IsAuthenticated != true)
            {
                return resultado; // X-API-KEY de PrestaShop (sin usuario): nunca
            }
            bool esClienteTienda = PermisosEventos.ClienteDelToken(usuario) != null;
            bool esDireccion = !esClienteTienda && usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.DIRECCION);
            string parametro = null;
            if (lector != null && ReglaAdelantarCupon.HayQueLeerParametro(esClienteTienda, esDireccion))
            {
                parametro = await lector.LeerParametro(PermisosEventos.SinDominio(usuario.Identity.Name)).ConfigureAwait(false);
            }
            AlcanceAdelantarCupon alcance = ReglaAdelantarCupon.Alcance(esClienteTienda, esDireccion, parametro);
            if (alcance == AlcanceAdelantarCupon.Todos)
            {
                foreach (CuponLeido c in lista)
                {
                    _ = resultado.Add(c.Id);
                }
                return resultado;
            }
            if (alcance != AlcanceAdelantarCupon.Equipo || lector == null)
            {
                return resultado;
            }
            string vendedor = lector.VendedorDelUsuario(usuario)?.Trim();
            if (string.IsNullOrEmpty(vendedor))
            {
                return resultado;
            }
            foreach (IGrouping<string, CuponLeido> porEmpresa in lista.GroupBy(c => string.IsNullOrWhiteSpace(c.Empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : c.Empresa.Trim()))
            {
                var equipo = new List<string> { vendedor };
                equipo.AddRange(await lector.VendedoresDelEquipo(porEmpresa.Key, vendedor).ConfigureAwait(false) ?? new List<string>());
                Dictionary<string, string> vendedores = await lector.VendedoresDeLosClientes(porEmpresa.Key,
                    porEmpresa.Select(c => new KeyValuePair<string, string>(c.Cliente, c.Contacto))).ConfigureAwait(false)
                    ?? new Dictionary<string, string>();
                foreach (CuponLeido c in porEmpresa)
                {
                    _ = vendedores.TryGetValue(LectorVendedoresInscripcion.ClaveClienteContacto(c.Cliente?.Trim(),
                        string.IsNullOrWhiteSpace(c.Contacto) ? null : c.Contacto.Trim()), out string delCliente);
                    if (ReglaAdelantarCupon.Puede(alcance, delCliente, equipo))
                    {
                        _ = resultado.Add(c.Id);
                    }
                }
            }
            return resultado;
        }
    }

    /// <summary>NestoAPI#885: la campana de Nesto a Administración de un cupón adelantado. Nunca lanza.</summary>
    public interface IAvisadorCuponAdelantado
    {
        Task Avisar(NotificacionPushDTO notificacion);
    }

    /// <summary>
    /// Al buzón de Nesto de cada uno del grupo Administración (GuardarEnBuzonDeUsuario: campana + SignalR), como
    /// <see cref="PedidosVenta.ConsultaReembolso.AvisadorConsultaReembolso"/>.
    /// </summary>
    public class AvisadorCuponAdelantado : IAvisadorCuponAdelantado
    {
        private const string DOMINIO = "NUEVAVISION\\";
        private readonly Func<IServicioNotificacionesPush> notificaciones;
        private readonly Func<List<string>> administracion;

        public AvisadorCuponAdelantado() : this(null, null)
        {
        }

        internal AvisadorCuponAdelantado(Func<IServicioNotificacionesPush> notificaciones, Func<List<string>> administracion)
        {
            this.notificaciones = notificaciones ?? (() => new ServicioNotificacionesPush());
            this.administracion = administracion ?? (() => MiembrosGrupoDominio.Leer(Constantes.GruposSeguridad.ADMINISTRACION));
        }

        public async Task Avisar(NotificacionPushDTO notificacion)
        {
            if (notificacion == null)
            {
                return;
            }
            try
            {
                List<string> usuarios = (administracion() ?? new List<string>())
                    .Select(u => u?.Trim())
                    .Where(u => !string.IsNullOrWhiteSpace(u) && !u.StartsWith("(", StringComparison.Ordinal))
                    .Select(u => u.Contains("\\") ? u : DOMINIO + u)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                IServicioNotificacionesPush servicio = notificaciones();
                foreach (string usuario in usuarios)
                {
                    await servicio.GuardarEnBuzonDeUsuario(usuario, Constantes.Aplicaciones.NESTO, notificacion).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Cupones #885] No se ha podido avisar a Administración del cupón adelantado: {ex.Message}", ex));
            }
        }
    }
}
