using NestoAPI.Infraestructure.Vendedores;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.ChequesRegalo
{
    /// <summary>
    /// NestoAPI#593 (estadística): la campaña vista desde la estadística por vendedor y el impulso en «clientes para
    /// contactar». <see cref="DiasImpulsoLlamadas"/> es la columna nueva de ChequesRegaloCampanas (script
    /// Issue593_ChequesRegalo_DiasImpulsoLlamadas.sql); sin la columna o a NULL vale
    /// <see cref="ReglasEstadisticaChequesRegalo.DIAS_IMPULSO_LLAMADAS_POR_DEFECTO"/>.
    /// </summary>
    public class CampanaEstadisticaChequeRegalo
    {
        public string Codigo { get; set; }
        public string Empresa { get; set; }
        public string Descripcion { get; set; }
        public decimal ImporteBase { get; set; }
        public DateTime GeneraDesde { get; set; }
        public DateTime GeneraHasta { get; set; }
        public DateTime CanjeHasta { get; set; }
        public bool Activa { get; set; }
        public int? DiasImpulsoLlamadas { get; set; }
    }

    /// <summary>
    /// Un cheque con su dueño (el vendedor de estética de la ficha: Clientes.Vendedor del contacto de la factura que lo
    /// generó, o del principal) y lo que hace falta para la barrida telefónica.
    /// </summary>
    public class ChequeRegaloVendedorFila : ChequeRegaloCliente
    {
        public string Contacto { get; set; }
        public string Vendedor { get; set; }
        public string NombreVendedor { get; set; }
        public string Nombre { get; set; }
        public string Direccion { get; set; }
        public string CodigoPostal { get; set; }
        public string Poblacion { get; set; }
        public string Provincia { get; set; }
        public string Telefono { get; set; }
        public string PersonaContacto { get; set; }
        public decimal? BaseImponibleFactura { get; set; }
        public decimal? TotalFactura { get; set; }
        /// <summary>Último rapport T/V/W en el que se habló con el cliente (Estado 0), cualquier contacto.</summary>
        public DateTime? UltimoContacto { get; set; }
        /// <summary>Último rapport T/V/W en el que no contestó (Estado 1).</summary>
        public DateTime? UltimoIntento { get; set; }
    }

    public class CampanaChequesRegaloResumenDTO
    {
        public string Codigo { get; set; }
        public string Descripcion { get; set; }
        /// <summary>Base imponible del cheque (50).</summary>
        public decimal Importe { get; set; }
        public DateTime GeneraDesde { get; set; }
        public DateTime GeneraHasta { get; set; }
        /// <summary>Último día (incluido) para usarlo.</summary>
        public DateTime CanjeHasta { get; set; }
        /// <summary>Días que faltan para el último día de canje (0 = hoy es el último día, o ya ha pasado).</summary>
        public int DiasRestantes { get; set; }
        public bool Activa { get; set; }
        /// <summary>Desde este día los clientes con el cheque sin usar suben en «clientes para contactar».</summary>
        public DateTime InicioImpulsoLlamadas { get; set; }
        public bool EnImpulsoLlamadas { get; set; }
    }

    /// <summary>Los cheques de un vendedor (o el total). Generados = Usados + Pendientes + Caducados (los anulados aparte).</summary>
    public class ResumenChequesVendedorDTO
    {
        public string Vendedor { get; set; }
        public string Nombre { get; set; }
        public int Generados { get; set; }
        /// <summary>EnPedido + Servidos.</summary>
        public int Usados { get; set; }
        /// <summary>Metidos en un pedido que todavía no se ha servido.</summary>
        public int EnPedido { get; set; }
        /// <summary>La línea del cheque ya está en albarán o factura.</summary>
        public int Servidos { get; set; }
        /// <summary>Sin usar y en plazo (incluye los que aún no se pueden usar, <see cref="PendientesDeActivar"/>).</summary>
        public int Pendientes { get; set; }
        public int PendientesDeActivar { get; set; }
        public int Caducados { get; set; }
        public int Anulados { get; set; }
    }

    /// <summary>GET api/ChequesRegalo/Resumen.</summary>
    public class ResumenChequesRegaloDTO
    {
        public CampanaChequesRegaloResumenDTO Campana { get; set; }
        public List<ResumenChequesVendedorDTO> Vendedores { get; set; } = new List<ResumenChequesVendedorDTO>();
        public ResumenChequesVendedorDTO Total { get; set; }
    }

    /// <summary>GET api/ChequesRegalo/Pendientes: un cheque, con el cliente para llamarle.</summary>
    public class ChequeRegaloPendienteDTO
    {
        public int Id { get; set; }
        public string Campana { get; set; }
        public string Empresa { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string Nombre { get; set; }
        public string PersonaContacto { get; set; }
        public string Direccion { get; set; }
        public string CodigoPostal { get; set; }
        public string Poblacion { get; set; }
        public string Provincia { get; set; }
        /// <summary>Tal cual la ficha: puede llevar varios separados por «/».</summary>
        public string Telefono { get; set; }
        public string Vendedor { get; set; }
        public string NombreVendedor { get; set; }
        /// <summary>Disponible, PendienteDeActivar, EnPedido, Canjeado, Caducado o Anulado (los de GET api/ChequesRegalo/Cliente).</summary>
        public string Estado { get; set; }
        public decimal Importe { get; set; }
        public DateTime? FechaActivacion { get; set; }
        public DateTime CanjeHasta { get; set; }
        /// <summary>Días que faltan para el último día de canje (0 = hoy es el último día). 0 en los que no están pendientes.</summary>
        public int DiasRestantes { get; set; }
        /// <summary>Empresa de la factura que lo generó (3 en la serie GB): la que hay que pasar a GET api/Facturas.</summary>
        public string EmpresaFactura { get; set; }
        public string FacturaOrigen { get; set; }
        public DateTime? FechaFactura { get; set; }
        public decimal? BaseImponibleFactura { get; set; }
        public decimal? TotalFactura { get; set; }
        public int? PedidoCanje { get; set; }
        public DateTime? FechaUltimoContacto { get; set; }
        public int? DiasDesdeUltimoContacto { get; set; }
        public DateTime? FechaUltimoIntento { get; set; }
    }

    /// <summary>Un cliente con el cheque sin usar al que hay que subir en «clientes para contactar».</summary>
    public class ChequeRegaloImpulso
    {
        public string Cliente { get; set; }
        /// <summary>El contacto de la factura que generó el cheque (o el principal).</summary>
        public string Contacto { get; set; }
        public DateTime InicioImpulso { get; set; }
        public DateTime CanjeHasta { get; set; }
        /// <summary>«Tiene un cheque de 50 € sin usar hasta el 07/11».</summary>
        public string Frase { get; set; }
    }

    /// <summary>Lo que el servicio de sugerencias de contacto necesita de los cheques.</summary>
    public interface IChequesRegaloParaSugerencias
    {
        /// <summary>Los clientes del vendedor con un cheque sin usar en una campaña que está en sus últimos días.
        /// Lista vacía fuera de esos días (y sin consultar los cheques).</summary>
        Task<List<ChequeRegaloImpulso>> LeerImpulsos(string vendedor, DateTime hoy);
    }

    public interface IRepositorioEstadisticaChequesRegalo
    {
        Task<List<CampanaEstadisticaChequeRegalo>> LeerCampanas(string empresa);
        Task<List<ChequeRegaloVendedorFila>> LeerCheques(string campana);
    }

    /// <summary>
    /// NestoAPI#593 (estadística): las reglas, sin base de datos.
    /// </summary>
    public static class ReglasEstadisticaChequesRegalo
    {
        /// <summary>Días antes del último día de canje (incluido) en que los clientes con el cheque sin usar suben en
        /// «clientes para contactar», si la campaña no dice otra cosa (ChequesRegaloCampanas.DiasImpulsoLlamadas).</summary>
        public const int DIAS_IMPULSO_LLAMADAS_POR_DEFECTO = 10;
        /// <summary>Sin campaña en la petición, vale la última empezada mientras no hayan pasado estos días desde el
        /// último día de canje (para ver cómo acabó).</summary>
        public const int DIAS_VISIBLE_TRAS_CANJE = 30;
        public const string SIN_VENDEDOR = "";

        private static readonly CultureInfo es = CultureInfo.GetCultureInfo("es-ES");

        public static int DiasImpulso(CampanaEstadisticaChequeRegalo campana)
            => campana.DiasImpulsoLlamadas.HasValue && campana.DiasImpulsoLlamadas.Value >= 0
                ? campana.DiasImpulsoLlamadas.Value
                : DIAS_IMPULSO_LLAMADAS_POR_DEFECTO;

        /// <summary>Con 10 días y canje hasta el 07/11: desde el 29/10 (los 10 últimos días, 07/11 incluido).</summary>
        public static DateTime InicioImpulso(CampanaEstadisticaChequeRegalo campana)
            => campana.CanjeHasta.Date.AddDays(1 - DiasImpulso(campana));

        public static bool EnImpulso(CampanaEstadisticaChequeRegalo campana, DateTime hoy)
            => campana.Activa && DiasImpulso(campana) > 0 && hoy.Date >= InicioImpulso(campana) && hoy.Date <= campana.CanjeHasta.Date;

        /// <summary>Días que faltan para el último día: el 06/11, 1; el mismo 07/11, 0 («último día»); después, 0.</summary>
        public static int DiasRestantes(DateTime canjeHasta, DateTime hoy)
            => Math.Max(0, (canjeHasta.Date - hoy.Date).Days);

        /// <summary>
        /// La campaña pedida o, sin pedir ninguna, la última que ya ha empezado a generar y cuyo canje no acabó hace más
        /// de <see cref="DIAS_VISIBLE_TRAS_CANJE"/> días. Null si no hay.
        /// </summary>
        public static CampanaEstadisticaChequeRegalo ElegirCampana(IEnumerable<CampanaEstadisticaChequeRegalo> campanas, string codigo, DateTime hoy)
        {
            List<CampanaEstadisticaChequeRegalo> lista = (campanas ?? Enumerable.Empty<CampanaEstadisticaChequeRegalo>()).Where(c => c != null).ToList();
            if (!string.IsNullOrWhiteSpace(codigo))
            {
                return lista.FirstOrDefault(c => string.Equals(c.Codigo?.Trim(), codigo.Trim(), StringComparison.OrdinalIgnoreCase));
            }
            return lista
                .Where(c => c.GeneraDesde.Date <= hoy.Date && c.CanjeHasta.Date.AddDays(DIAS_VISIBLE_TRAS_CANJE) >= hoy.Date)
                .OrderByDescending(c => c.CanjeHasta)
                .ThenByDescending(c => c.GeneraDesde)
                .FirstOrDefault();
        }

        public static string Estado(ChequeRegaloVendedorFila cheque, CampanaEstadisticaChequeRegalo campana, DateTime ahora)
            => ReglasCanjeChequeRegalo.EstadoParaCliente(cheque, new CampanaChequeRegalo { CanjeHasta = campana.CanjeHasta }, ahora);

        public static bool EsPendiente(string estado) => estado == "Disponible" || estado == "PendienteDeActivar";

        public static string Vendedor(string vendedor) => vendedor?.Trim().ToUpperInvariant() ?? SIN_VENDEDOR;

        public static CampanaChequesRegaloResumenDTO CampanaDTO(CampanaEstadisticaChequeRegalo campana, DateTime hoy) => new CampanaChequesRegaloResumenDTO
        {
            Codigo = campana.Codigo?.Trim(),
            Descripcion = campana.Descripcion?.Trim(),
            Importe = campana.ImporteBase,
            GeneraDesde = campana.GeneraDesde,
            GeneraHasta = campana.GeneraHasta,
            CanjeHasta = campana.CanjeHasta,
            DiasRestantes = DiasRestantes(campana.CanjeHasta, hoy),
            Activa = campana.Activa,
            InicioImpulsoLlamadas = InicioImpulso(campana),
            EnImpulsoLlamadas = EnImpulso(campana, hoy)
        };

        /// <summary>
        /// Una fila por vendedor. Con <paramref name="visibles"/> (jefe de ventas o vendedor) solo cuentan los cheques de
        /// esos vendedores y salen todos, aunque no tengan ninguno; sin ellos (Dirección), los de todos los cheques, con
        /// «(sin vendedor)» si algún cliente no tiene. Ordenadas por nombre; el total suma las filas.
        /// </summary>
        public static ResumenChequesRegaloDTO Resumir(CampanaEstadisticaChequeRegalo campana, IEnumerable<ChequeRegaloVendedorFila> cheques,
            IList<VendedorVisibleDTO> visibles, DateTime ahora)
        {
            var filas = new Dictionary<string, ResumenChequesVendedorDTO>(StringComparer.OrdinalIgnoreCase);
            ResumenChequesVendedorDTO Fila(string vendedor, string nombre)
            {
                if (filas.TryGetValue(vendedor, out ResumenChequesVendedorDTO fila))
                {
                    if (fila.Nombre == fila.Vendedor && !string.IsNullOrWhiteSpace(nombre))
                    {
                        fila.Nombre = nombre.Trim();
                    }
                }
                else
                {
                    fila = new ResumenChequesVendedorDTO
                    {
                        Vendedor = vendedor,
                        Nombre = vendedor == SIN_VENDEDOR ? "(sin vendedor)" : string.IsNullOrWhiteSpace(nombre) ? vendedor : nombre.Trim()
                    };
                    filas[vendedor] = fila;
                }
                return fila;
            }

            HashSet<string> permitidos = null;
            if (visibles != null)
            {
                permitidos = new HashSet<string>(visibles.Select(v => Vendedor(v.Vendedor)), StringComparer.OrdinalIgnoreCase);
                foreach (VendedorVisibleDTO v in visibles)
                {
                    _ = Fila(Vendedor(v.Vendedor), v.Nombre);
                }
            }

            foreach (ChequeRegaloVendedorFila cheque in cheques ?? Enumerable.Empty<ChequeRegaloVendedorFila>())
            {
                string vendedor = Vendedor(cheque.Vendedor);
                if (permitidos != null && !permitidos.Contains(vendedor))
                {
                    continue;
                }
                Sumar(Fila(vendedor, cheque.NombreVendedor), Estado(cheque, campana, ahora));
            }

            List<ResumenChequesVendedorDTO> ordenadas = filas.Values
                .OrderBy(f => f.Vendedor == SIN_VENDEDOR ? 1 : 0)
                .ThenBy(f => f.Nombre, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(f => f.Vendedor, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return new ResumenChequesRegaloDTO
            {
                Campana = CampanaDTO(campana, ahora),
                Vendedores = ordenadas,
                Total = new ResumenChequesVendedorDTO
                {
                    Vendedor = null,
                    Nombre = "Total",
                    Generados = ordenadas.Sum(f => f.Generados),
                    Usados = ordenadas.Sum(f => f.Usados),
                    EnPedido = ordenadas.Sum(f => f.EnPedido),
                    Servidos = ordenadas.Sum(f => f.Servidos),
                    Pendientes = ordenadas.Sum(f => f.Pendientes),
                    PendientesDeActivar = ordenadas.Sum(f => f.PendientesDeActivar),
                    Caducados = ordenadas.Sum(f => f.Caducados),
                    Anulados = ordenadas.Sum(f => f.Anulados)
                }
            };
        }

        private static void Sumar(ResumenChequesVendedorDTO fila, string estado)
        {
            switch (estado)
            {
                case "Anulado":
                    fila.Anulados++;
                    return;
                case "EnPedido":
                    fila.EnPedido++;
                    fila.Usados++;
                    break;
                case "Canjeado":
                    fila.Servidos++;
                    fila.Usados++;
                    break;
                case "Caducado":
                    fila.Caducados++;
                    break;
                case "PendienteDeActivar":
                    fila.PendientesDeActivar++;
                    fila.Pendientes++;
                    break;
                default:
                    fila.Pendientes++;
                    break;
            }
            fila.Generados++;
        }

        /// <summary>
        /// Los cheques sin usar (o todos, con <paramref name="todos"/>) de los vendedores permitidos (null = todos), los
        /// que caducan antes primero y, dentro, por vendedor y nombre del cliente.
        /// </summary>
        public static List<ChequeRegaloPendienteDTO> Pendientes(CampanaEstadisticaChequeRegalo campana, IEnumerable<ChequeRegaloVendedorFila> cheques,
            ISet<string> vendedores, bool todos, DateTime ahora)
        {
            return (cheques ?? Enumerable.Empty<ChequeRegaloVendedorFila>())
                .Where(c => vendedores == null || vendedores.Contains(Vendedor(c.Vendedor)))
                .Select(c => new { Cheque = c, Estado = Estado(c, campana, ahora) })
                .Where(x => todos || EsPendiente(x.Estado))
                .Select(x => ADTO(x.Cheque, x.Estado, campana, ahora))
                .OrderBy(d => d.DiasRestantes)
                .ThenBy(d => d.NombreVendedor ?? d.Vendedor, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(d => d.Nombre, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(d => d.Cliente, StringComparer.Ordinal)
                .ToList();
        }

        internal static ChequeRegaloPendienteDTO ADTO(ChequeRegaloVendedorFila c, string estado, CampanaEstadisticaChequeRegalo campana, DateTime ahora) => new ChequeRegaloPendienteDTO
        {
            Id = c.Id,
            Campana = c.Campana?.Trim(),
            Empresa = c.Empresa?.Trim(),
            Cliente = c.Cliente?.Trim(),
            Contacto = c.Contacto?.Trim(),
            Nombre = c.Nombre?.Trim(),
            PersonaContacto = c.PersonaContacto?.Trim(),
            Direccion = c.Direccion?.Trim(),
            CodigoPostal = c.CodigoPostal?.Trim(),
            Poblacion = c.Poblacion?.Trim(),
            Provincia = c.Provincia?.Trim(),
            Telefono = c.Telefono?.Trim(),
            Vendedor = c.Vendedor?.Trim(),
            NombreVendedor = c.NombreVendedor?.Trim(),
            Estado = estado,
            Importe = campana.ImporteBase,
            FechaActivacion = c.FechaActivacion,
            CanjeHasta = campana.CanjeHasta,
            DiasRestantes = EsPendiente(estado) ? DiasRestantes(campana.CanjeHasta, ahora) : 0,
            EmpresaFactura = c.EmpresaFactura?.Trim(),
            FacturaOrigen = c.FacturaOrigen?.Trim(),
            FechaFactura = c.FechaFactura,
            BaseImponibleFactura = c.BaseImponibleFactura,
            TotalFactura = c.TotalFactura,
            PedidoCanje = c.PedidoCanje,
            FechaUltimoContacto = c.UltimoContacto,
            DiasDesdeUltimoContacto = c.UltimoContacto.HasValue ? (int?)(ahora.Date - c.UltimoContacto.Value.Date).Days : null,
            FechaUltimoIntento = c.UltimoIntento
        };

        /// <summary>«Tiene un cheque de 50 € sin usar hasta el 07/11».</summary>
        public static string FraseImpulso(CampanaEstadisticaChequeRegalo campana)
        {
            string importe = campana.ImporteBase == Math.Truncate(campana.ImporteBase)
                ? campana.ImporteBase.ToString("N0", es)
                : campana.ImporteBase.ToString("N2", es);
            return $"Tiene un cheque de {importe} € sin usar hasta el {campana.CanjeHasta.ToString("dd/MM", es)}";
        }

        /// <summary>Los cheques que se pueden usar ya (Disponible) del vendedor, en los últimos días de la campaña.</summary>
        public static List<ChequeRegaloImpulso> Impulsos(CampanaEstadisticaChequeRegalo campana, IEnumerable<ChequeRegaloVendedorFila> cheques,
            string vendedor, DateTime ahora)
        {
            if (campana == null || !EnImpulso(campana, ahora))
            {
                return new List<ChequeRegaloImpulso>();
            }
            string propio = Vendedor(vendedor);
            string frase = FraseImpulso(campana);
            return (cheques ?? Enumerable.Empty<ChequeRegaloVendedorFila>())
                .Where(c => Vendedor(c.Vendedor) == propio && Estado(c, campana, ahora) == "Disponible")
                .Select(c => new ChequeRegaloImpulso
                {
                    Cliente = c.Cliente?.Trim(),
                    Contacto = c.Contacto?.Trim(),
                    InicioImpulso = InicioImpulso(campana),
                    CanjeHasta = campana.CanjeHasta,
                    Frase = frase
                })
                .ToList();
        }
    }

    /// <summary>
    /// NestoAPI#593 (estadística): resumen por vendedor, lista de pendientes para la barrida telefónica e impulso en
    /// «clientes para contactar». Quién puede ver qué lo decide el controlador (<see cref="IServicioVendedoresVisibles"/>).
    /// </summary>
    public class ServicioEstadisticaChequesRegalo : IChequesRegaloParaSugerencias
    {
        private readonly IRepositorioEstadisticaChequesRegalo repositorio;

        public ServicioEstadisticaChequesRegalo(IRepositorioEstadisticaChequesRegalo repositorio)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
        }

        public async Task<CampanaEstadisticaChequeRegalo> LeerCampana(string empresa, string campana, DateTime hoy)
        {
            List<CampanaEstadisticaChequeRegalo> campanas = await repositorio.LeerCampanas(empresa).ConfigureAwait(false);
            return ReglasEstadisticaChequesRegalo.ElegirCampana(campanas, campana, hoy);
        }

        /// <param name="visibles">Null = todos los vendedores (Dirección).</param>
        public async Task<ResumenChequesRegaloDTO> LeerResumen(string empresa, string campana, IList<VendedorVisibleDTO> visibles, DateTime ahora)
        {
            CampanaEstadisticaChequeRegalo elegida = await LeerCampana(empresa, campana, ahora).ConfigureAwait(false);
            if (elegida == null)
            {
                return null;
            }
            List<ChequeRegaloVendedorFila> cheques = await repositorio.LeerCheques(elegida.Codigo).ConfigureAwait(false);
            return ReglasEstadisticaChequesRegalo.Resumir(elegida, cheques, visibles, ahora);
        }

        /// <param name="vendedores">Null = todos los vendedores (Dirección).</param>
        public async Task<List<ChequeRegaloPendienteDTO>> LeerPendientes(string empresa, string campana, ISet<string> vendedores, bool todos, DateTime ahora)
        {
            CampanaEstadisticaChequeRegalo elegida = await LeerCampana(empresa, campana, ahora).ConfigureAwait(false);
            if (elegida == null)
            {
                return null;
            }
            List<ChequeRegaloVendedorFila> cheques = await repositorio.LeerCheques(elegida.Codigo).ConfigureAwait(false);
            return ReglasEstadisticaChequesRegalo.Pendientes(elegida, cheques, vendedores, todos, ahora);
        }

        public async Task<List<ChequeRegaloImpulso>> LeerImpulsos(string vendedor, DateTime hoy)
        {
            var resultado = new List<ChequeRegaloImpulso>();
            if (string.IsNullOrWhiteSpace(vendedor))
            {
                return resultado;
            }
            List<CampanaEstadisticaChequeRegalo> campanas = await repositorio.LeerCampanas(Constantes.Empresas.EMPRESA_POR_DEFECTO).ConfigureAwait(false);
            foreach (CampanaEstadisticaChequeRegalo campana in campanas.Where(c => ReglasEstadisticaChequesRegalo.EnImpulso(c, hoy)))
            {
                List<ChequeRegaloVendedorFila> cheques = await repositorio.LeerCheques(campana.Codigo).ConfigureAwait(false);
                resultado.AddRange(ReglasEstadisticaChequesRegalo.Impulsos(campana, cheques, vendedor, hoy));
            }
            return resultado;
        }
    }

    /// <summary>NestoAPI#593 (estadística): SQL directo (sin EDMX), como la generación y el canje. Solo lectura.</summary>
    public class RepositorioEstadisticaChequesRegalo : IRepositorioEstadisticaChequesRegalo
    {
        private readonly NVEntities db;

        public RepositorioEstadisticaChequesRegalo(NVEntities db)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
        }

        // Sin tablas: no hay campañas. Sin la columna DiasImpulsoLlamadas (script sin lanzar): NULL, que es el valor por
        // defecto. La columna nueva solo se nombra en SQL dinámico (si no existe, no compila ni en la rama que no se ejecuta).
        internal const string SQL_CAMPANAS = @"
IF OBJECT_ID('dbo.ChequesRegaloCampanas') IS NULL
    SELECT CAST(NULL AS varchar(20)) AS Codigo, CAST(NULL AS char(3)) AS Empresa, CAST(NULL AS nvarchar(200)) AS Descripcion,
           CAST(0 AS decimal(10,2)) AS ImporteBase, GETDATE() AS GeneraDesde, GETDATE() AS GeneraHasta, GETDATE() AS CanjeHasta,
           CAST(0 AS bit) AS Activa, CAST(NULL AS int) AS DiasImpulsoLlamadas
    WHERE 1 = 0
ELSE IF COL_LENGTH('dbo.ChequesRegaloCampanas', 'DiasImpulsoLlamadas') IS NULL
    SELECT RTRIM(Codigo) AS Codigo, RTRIM(Empresa) AS Empresa, RTRIM(Descripcion) AS Descripcion, ImporteBase,
           CAST(GeneraDesde AS datetime) AS GeneraDesde, CAST(GeneraHasta AS datetime) AS GeneraHasta, CAST(CanjeHasta AS datetime) AS CanjeHasta,
           Activa, CAST(NULL AS int) AS DiasImpulsoLlamadas
    FROM dbo.ChequesRegaloCampanas WHERE Empresa = @p0
ELSE
    EXEC sp_executesql N'SELECT RTRIM(Codigo) AS Codigo, RTRIM(Empresa) AS Empresa, RTRIM(Descripcion) AS Descripcion, ImporteBase,
           CAST(GeneraDesde AS datetime) AS GeneraDesde, CAST(GeneraHasta AS datetime) AS GeneraHasta, CAST(CanjeHasta AS datetime) AS CanjeHasta,
           Activa, DiasImpulsoLlamadas
    FROM dbo.ChequesRegaloCampanas WHERE Empresa = @Empresa', N'@Empresa char(3)', @Empresa = @p0";

        // El dueño: Clientes.Vendedor (el de estética; el de peluquería va aparte, en VendedoresClienteGrupoProducto) del
        // contacto de la factura que generó el cheque, o del principal. Los clientes viven en la empresa del cheque (1)
        // aunque la factura sea de la espejo (GB → 3). Los rapports, de cualquier contacto y de los 60 días antes.
        // 600 cheques como mucho: unos milisegundos (13/10/26, 5 cheques: 6 ms).
        internal const string SQL_CHEQUES = @"
SELECT c.Id, RTRIM(c.Campana) AS Campana, RTRIM(c.Empresa) AS Empresa, RTRIM(c.Cliente) AS Cliente,
       RTRIM(c.EmpresaFactura) AS EmpresaFactura, RTRIM(c.FacturaOrigen) AS FacturaOrigen, c.FechaFactura, c.FechaGeneracion,
       c.FechaActivacion, RTRIM(c.Estado) AS Estado, RTRIM(c.EmpresaCanje) AS EmpresaCanje, c.PedidoCanje, c.FechaCanje,
       CAST(CASE WHEN c.PedidoCanje IS NOT NULL AND EXISTS (SELECT 1 FROM dbo.LinPedidoVta l WHERE l.[Número] = c.PedidoCanje AND l.Producto = k.Producto)
                 THEN 1 ELSE 0 END AS bit) AS PedidoCanjeTieneLaLinea,
       CAST(CASE WHEN c.PedidoCanje IS NOT NULL AND EXISTS (SELECT 1 FROM dbo.LinPedidoVta l WHERE l.[Número] = c.PedidoCanje AND l.Producto = k.Producto AND l.Estado >= 2)
                 THEN 1 ELSE 0 END AS bit) AS LineaChequeServida,
       CAST(0 AS bit) AS EsPedidoDeLaFacturaOrigen,
       RTRIM(cl.Contacto) AS Contacto, RTRIM(cl.Vendedor) AS Vendedor, RTRIM(v.[Descripción]) AS NombreVendedor,
       RTRIM(cl.Nombre) AS Nombre, RTRIM(cl.[Dirección]) AS Direccion, RTRIM(cl.CodPostal) AS CodigoPostal,
       RTRIM(cl.[Población]) AS Poblacion, RTRIM(cl.Provincia) AS Provincia, RTRIM(cl.[Teléfono]) AS Telefono,
       (SELECT TOP 1 COALESCE(NULLIF(RTRIM(p.Saludo), ''), RTRIM(p.Nombre)) FROM dbo.PersonasContactoCliente p
        WHERE p.Empresa = cl.Empresa AND p.[NºCliente] = cl.[Nº Cliente] AND p.Contacto = cl.Contacto AND p.Estado >= 0
              AND (RTRIM(p.Saludo) <> '' OR RTRIM(p.Nombre) <> '') ORDER BY p.[Número]) AS PersonaContacto,
       f.BaseImponibleFactura, f.TotalFactura,
       s.UltimoContacto, s.UltimoIntento
FROM dbo.ChequesRegalo c
     INNER JOIN dbo.ChequesRegaloCampanas k ON k.Codigo = c.Campana
     OUTER APPLY (SELECT TOP 1 x.Empresa, x.[Nº Cliente], x.Contacto, x.Vendedor, x.Nombre, x.[Dirección], x.CodPostal, x.[Población], x.Provincia, x.[Teléfono]
                  FROM dbo.Clientes x
                  WHERE x.Empresa = c.Empresa AND x.[Nº Cliente] = c.Cliente
                  ORDER BY CASE WHEN x.Contacto = (SELECT cf.Contacto FROM dbo.CabFacturaVta cf WHERE cf.Empresa = c.EmpresaFactura AND cf.[Número] = c.FacturaOrigen) THEN 0 ELSE 1 END,
                           CASE WHEN x.ClientePrincipal = 1 THEN 0 ELSE 1 END, x.Contacto) cl
     LEFT JOIN dbo.Vendedores v ON v.Empresa = c.Empresa AND v.[Número] = cl.Vendedor
     OUTER APPLY (SELECT SUM(l.[Base Imponible]) AS BaseImponibleFactura, SUM(l.Total) AS TotalFactura
                  FROM dbo.LinPedidoVta l WHERE l.Empresa = c.EmpresaFactura AND l.[Nº Factura] = c.FacturaOrigen) f
     OUTER APPLY (SELECT MAX(CASE WHEN sc.Estado = 0 THEN sc.Fecha END) AS UltimoContacto, MAX(CASE WHEN sc.Estado = 1 THEN sc.Fecha END) AS UltimoIntento
                  FROM dbo.SeguimientoCliente sc
                  WHERE sc.[Número] = c.Cliente AND sc.Estado IN (0, 1) AND sc.Tipo IN ('T', 'V', 'W') AND sc.Fecha >= c.FechaGeneracion - 60) s
WHERE c.Campana = @p0";

        public async Task<List<CampanaEstadisticaChequeRegalo>> LeerCampanas(string empresa)
            => await db.Database.SqlQuery<CampanaEstadisticaChequeRegalo>(SQL_CAMPANAS,
                new SqlParameter("@p0", string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim()))
                .ToListAsync().ConfigureAwait(false);

        public async Task<List<ChequeRegaloVendedorFila>> LeerCheques(string campana)
            => await db.Database.SqlQuery<ChequeRegaloVendedorFila>(SQL_CHEQUES, new SqlParameter("@p0", campana?.Trim() ?? string.Empty))
                .ToListAsync().ConfigureAwait(false);
    }
}
