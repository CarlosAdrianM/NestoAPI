using NestoAPI.Infraestructure.Agencias.RepartoPropio;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Reposiciones
{
    /// <summary>NestoAPI#772: GET api/Reposiciones/{numTraspaso}/Seguimiento. La historia de una reposición y de cada uno de sus bultos.</summary>
    public class SeguimientoReposicionDTO
    {
        public string Empresa { get; set; }
        public int Traspaso { get; set; }
        public string Origen { get; set; }
        public string Destino { get; set; }
        public string Herramienta { get; set; }
        /// <summary>Del traspaso: Creada, Preparada, Recogida, Recibida (por FechaHora).</summary>
        public List<EventoSeguimientoReposicionDTO> Eventos { get; set; } = new List<EventoSeguimientoReposicionDTO>();
        public List<BultoSeguimientoReposicionDTO> Bultos { get; set; } = new List<BultoSeguimientoReposicionDTO>();
    }

    public class BultoSeguimientoReposicionDTO
    {
        public string Codigo { get; set; }
        public int Bulto { get; set; }
        public int Total { get; set; }
        /// <summary>Dónde está ahora, para enseñarlo tal cual: «En la furgoneta de Andre», «Recibido en Algete»…</summary>
        public string Estado { get; set; }
        /// <summary>Etiquetado, Cargado, Entregado, Recogido, Descargado, Recibido (por FechaHora).</summary>
        public List<EventoSeguimientoReposicionDTO> Eventos { get; set; } = new List<EventoSeguimientoReposicionDTO>();
    }

    public class EventoSeguimientoReposicionDTO
    {
        public const string CREADA = "Creada";
        public const string PREPARADA = "Preparada";
        public const string RECOGIDA = "Recogida";
        public const string RECIBIDA = "Recibida";
        public const string ETIQUETADO = "Etiquetado";
        public const string RECIBIDO = "Recibido";

        public DateTime FechaHora { get; set; }
        public string Tipo { get; set; }
        /// <summary>Sin dominio.</summary>
        public string Usuario { get; set; }
        /// <summary>«Repartidor» o «Almacen» en los de la furgoneta; null en los demás.</summary>
        public string Origen { get; set; }
        public string Detalle { get; set; }
    }

    /// <summary>NestoAPI#772: la cabecera de ReposicionesTraspasos (las columnas de la recogida pueden no existir: null).</summary>
    public class CabeceraSeguimientoReposicion
    {
        public string Origen { get; set; }
        public string Destino { get; set; }
        public string Herramienta { get; set; }
        public string UsuarioCreacion { get; set; }
        public DateTime? FechaCreacion { get; set; }
        public string UsuarioPreparacion { get; set; }
        public DateTime? FechaPreparada { get; set; }
        public string UsuarioRecepcion { get; set; }
        public DateTime? FechaRecibida { get; set; }
        public DateTime? FechaRecogida { get; set; }
        public string UsuarioRecogida { get; set; }
    }

    /// <summary>NestoAPI#772: una fila de ReposicionesBultos con quién la etiquetó.</summary>
    public class FilaBultoSeguimiento
    {
        public string Codigo { get; set; }
        public int Bulto { get; set; }
        public int Total { get; set; }
        public DateTime? FechaCreacion { get; set; }
        public string UsuarioCreacion { get; set; }
        public DateTime? FechaLectura { get; set; }
        public string UsuarioLectura { get; set; }
    }

    /// <summary>NestoAPI#772: una carga en RepartoParadasReposicionBultos (de antes del script de la furgoneta) con su parada.</summary>
    public class CargaAntiguaSeguimiento
    {
        public int Bulto { get; set; }
        public string Usuario { get; set; }
        public DateTime FechaHora { get; set; }
        public Guid ClaveIdempotencia { get; set; }
        public string Destino { get; set; }
        public DateTime? FechaEntrega { get; set; }
        public string UsuarioEntrega { get; set; }
        public string Receptor { get; set; }
    }

    public interface IRepositorioSeguimientoReposiciones
    {
        /// <summary>Null si el traspaso no tiene cabecera de la API.</summary>
        Task<CabeceraSeguimientoReposicion> LeerCabecera(string empresa, int traspaso);
        /// <summary>Null si no existe ReposicionesBultos.</summary>
        Task<List<FilaBultoSeguimiento>> LeerBultos(string empresa, int traspaso);
        /// <summary>Vacía sin el script de la furgoneta.</summary>
        Task<List<MovimientoBultoReparto>> LeerMovimientos(IReadOnlyCollection<int> traspasos);
        /// <summary>Vacía sin el script de las paradas.</summary>
        Task<List<CargaAntiguaSeguimiento>> LeerCargasParadas(string empresa, int traspaso);
        /// <summary>
        /// Los traspasos del viaje de hoy de la ruta: los del job con el corte <paramref name="diaCorte"/> y los que se han hecho a mano
        /// (Nesto, Ariadna, puntual) hoy.
        /// </summary>
        Task<List<int>> TraspasosDelViaje(string empresa, string origen, string destino, DateTime diaCorte, DateTime hoy);
        /// <summary>Los bultos (ReposicionesBultos) de esos traspasos. Vacía sin la tabla.</summary>
        Task<List<FilaBultoReposicion>> LeerBultosDe(string empresa, IReadOnlyCollection<int> traspasos);
        bool FurgonetaDisponible();
    }

    /// <summary>
    /// NestoAPI#772: el seguimiento de una reposición (traspaso y bultos) y, en GET Puntual/Estado, cuántos bultos de la de hoy ha
    /// recogido ya el repartidor (para «Recogidos 6 de 7 bultos por Andre» en Nesto). Lee todo lo que hay: cabecera de la API,
    /// etiquetas, movimientos de la furgoneta y, para lo de antes del script, las cargas de las paradas de reposición.
    /// </summary>
    public class ServicioSeguimientoReposiciones
    {
        private readonly IRepositorioSeguimientoReposiciones repositorio;
        private readonly Func<DateTime> ahora;

        public ServicioSeguimientoReposiciones(IRepositorioSeguimientoReposiciones repositorio, Func<DateTime> ahora = null)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
            this.ahora = ahora ?? (() => DateTime.Now);
        }

        /// <summary>Null si no se sabe nada del traspaso (ni cabecera, ni etiquetas, ni movimientos).</summary>
        public async Task<SeguimientoReposicionDTO> Seguimiento(string empresa, int traspaso)
        {
            empresa = string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();
            CabeceraSeguimientoReposicion cabecera = await repositorio.LeerCabecera(empresa, traspaso).ConfigureAwait(false);
            List<FilaBultoSeguimiento> bultos = await repositorio.LeerBultos(empresa, traspaso).ConfigureAwait(false) ?? new List<FilaBultoSeguimiento>();
            List<MovimientoBultoReparto> movimientos = (await repositorio.LeerMovimientos(new[] { traspaso }).ConfigureAwait(false) ?? new List<MovimientoBultoReparto>())
                .Where(m => m.TipoCodigo == CodigoBultoFurgoneta.R && m.NumTraspaso == traspaso).ToList();
            List<CargaAntiguaSeguimiento> cargas = await repositorio.LeerCargasParadas(empresa, traspaso).ConfigureAwait(false) ?? new List<CargaAntiguaSeguimiento>();
            if (cabecera == null && bultos.Count == 0 && movimientos.Count == 0 && cargas.Count == 0)
            {
                return null;
            }
            string origen = cabecera?.Origen?.Trim();
            string destino = cabecera?.Destino?.Trim();
            var dto = new SeguimientoReposicionDTO
            {
                Empresa = empresa,
                Traspaso = traspaso,
                Origen = origen,
                Destino = destino,
                Herramienta = cabecera?.Herramienta?.Trim()
            };
            if (cabecera != null)
            {
                Anadir(dto.Eventos, cabecera.FechaCreacion, EventoSeguimientoReposicionDTO.CREADA, cabecera.UsuarioCreacion, null, cabecera.Herramienta?.Trim());
                Anadir(dto.Eventos, cabecera.FechaPreparada, EventoSeguimientoReposicionDTO.PREPARADA, cabecera.UsuarioPreparacion, null, "Enviada desde " + Nombre(origen));
                Anadir(dto.Eventos, cabecera.FechaRecogida, EventoSeguimientoReposicionDTO.RECOGIDA, cabecera.UsuarioRecogida, null, "El repartidor se la ha llevado");
                Anadir(dto.Eventos, cabecera.FechaRecibida, EventoSeguimientoReposicionDTO.RECIBIDA, cabecera.UsuarioRecepcion, null, "Recibida en " + Nombre(destino));
                dto.Eventos = dto.Eventos.OrderBy(e => e.FechaHora).ToList();
            }

            var numeros = new SortedSet<int>(bultos.Select(b => b.Bulto).Concat(movimientos.Select(m => m.Bulto)).Concat(cargas.Select(c => c.Bulto)));
            int total = bultos.Select(b => b.Total).Concat(movimientos.Where(m => m.Total.HasValue).Select(m => m.Total.Value)).DefaultIfEmpty(numeros.Count).Max();
            var claves = new HashSet<Guid>(movimientos.Select(m => m.ClaveIdempotencia));
            foreach (int n in numeros)
            {
                string codigo = CodigosBultoReposicion.Codigo(traspaso, n);
                FilaBultoSeguimiento etiqueta = bultos.FirstOrDefault(b => b.Bulto == n);
                var bulto = new BultoSeguimientoReposicionDTO { Codigo = codigo, Bulto = n, Total = Math.Max(total, n) };
                if (etiqueta != null)
                {
                    Anadir(bulto.Eventos, etiqueta.FechaCreacion, EventoSeguimientoReposicionDTO.ETIQUETADO, etiqueta.UsuarioCreacion, null, "Etiqueta impresa en " + Nombre(origen));
                    Anadir(bulto.Eventos, etiqueta.FechaLectura, EventoSeguimientoReposicionDTO.RECIBIDO, etiqueta.UsuarioLectura, null, "Leído al recibir en " + Nombre(destino));
                }
                List<MovimientoBultoReparto> suyos = movimientos.Where(m => m.Bulto == n).ToList();
                foreach (MovimientoBultoReparto m in suyos)
                {
                    Anadir(bulto.Eventos, m.FechaHora, m.Tipo, m.Usuario, m.Origen, DetalleMovimiento(m, origen, destino));
                }
                // Lo de antes del script de la furgoneta: las cargas en las paradas de reposición y su entrega
                foreach (CargaAntiguaSeguimiento c in cargas.Where(c => c.Bulto == n && !claves.Contains(c.ClaveIdempotencia)))
                {
                    Anadir(bulto.Eventos, c.FechaHora, TiposMovimientoBulto.CARGADO, c.Usuario, OrigenesMovimientoBulto.REPARTIDOR, "Parada «Reposición " + Nombre(c.Destino) + "»");
                }
                if (!suyos.Any(m => m.Tipo == TiposMovimientoBulto.ENTREGADO))
                {
                    CargaAntiguaSeguimiento entregada = cargas.Where(c => c.Bulto == n && c.FechaEntrega.HasValue).OrderBy(c => c.FechaEntrega).LastOrDefault();
                    if (entregada != null)
                    {
                        Anadir(bulto.Eventos, entregada.FechaEntrega, TiposMovimientoBulto.ENTREGADO, entregada.UsuarioEntrega, OrigenesMovimientoBulto.REPARTIDOR,
                            "En " + Nombre(entregada.Destino) + (string.IsNullOrWhiteSpace(entregada.Receptor) ? string.Empty : $" (lo recogió {entregada.Receptor.Trim()})"));
                    }
                }
                bulto.Eventos = bulto.Eventos.OrderBy(e => e.FechaHora).ThenBy(e => Orden(e.Tipo)).ToList();
                bulto.Estado = EstadoBulto(bulto.Eventos, origen, destino);
                dto.Bultos.Add(bulto);
            }
            return dto;
        }

        /// <summary>
        /// GET Puntual/Estado (tienda → Algete): los bultos de las reposiciones del viaje de hoy, cuántos ha recogido ya el repartidor,
        /// quién y cuándo el último. Sin la furgoneta, no toca nada. Nunca lanza.
        /// </summary>
        public async Task CompletarRecogida(EstadoReposicionPuntualDTO estado, string empresa)
        {
            if (estado == null || string.IsNullOrWhiteSpace(estado.Origen))
            {
                return;
            }
            try
            {
                if (!repositorio.FurgonetaDisponible())
                {
                    return;
                }
                empresa = string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();
                DateTime hoy = ahora().Date;
                List<int> traspasos = await repositorio.TraspasosDelViaje(empresa, estado.Origen.Trim(), (estado.Destino ?? Constantes.Almacenes.ALGETE).Trim(),
                    estado.CierreHoy?.Date ?? hoy, hoy).ConfigureAwait(false) ?? new List<int>();
                if (traspasos.Count == 0)
                {
                    estado.BultosTotal = 0;
                    estado.BultosRecogidos = 0;
                    return;
                }
                List<FilaBultoReposicion> bultos = await repositorio.LeerBultosDe(empresa, traspasos).ConfigureAwait(false) ?? new List<FilaBultoReposicion>();
                List<MovimientoBultoReparto> recogidos = (await repositorio.LeerMovimientos(traspasos).ConfigureAwait(false) ?? new List<MovimientoBultoReparto>())
                    .Where(m => m.TipoCodigo == CodigoBultoFurgoneta.R && m.Tipo == TiposMovimientoBulto.RECOGIDO && m.NumTraspaso.HasValue && traspasos.Contains(m.NumTraspaso.Value))
                    .ToList();
                estado.BultosTotal = bultos.GroupBy(b => b.Traspaso).Sum(g => g.Max(b => (int)b.Total));
                estado.BultosRecogidos = recogidos.Select(m => (m.NumTraspaso, m.Bulto)).Distinct().Count();
                MovimientoBultoReparto ultimo = recogidos.OrderBy(m => m.FechaHora).ThenBy(m => m.Id).LastOrDefault();
                estado.RepartidorRecogida = ServicioRepartoPropio.NombreRepartidor(ultimo?.Usuario);
                estado.FechaUltimoBultoRecogido = ultimo?.FechaHora;
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Furgoneta #772] No se pudo saber cuántos bultos de la reposición de {estado.Origen} se han recogido: {ex.Message}", ex));
            }
        }

        private static void Anadir(List<EventoSeguimientoReposicionDTO> eventos, DateTime? fecha, string tipo, string usuario, string origen, string detalle)
        {
            if (!fecha.HasValue)
            {
                return;
            }
            eventos.Add(new EventoSeguimientoReposicionDTO
            {
                FechaHora = fecha.Value,
                Tipo = tipo,
                Usuario = ServicioRepartoPropio.NombreRepartidor(usuario),
                Origen = origen,
                Detalle = detalle
            });
        }

        private static string DetalleMovimiento(MovimientoBultoReparto m, string origen, string destino)
        {
            bool almacen = m.Origen == OrigenesMovimientoBulto.ALMACEN;
            switch (m.Tipo)
            {
                case TiposMovimientoBulto.CARGADO: return "Cargado en la furgoneta en " + Nombre(origen);
                case TiposMovimientoBulto.RECOGIDO: return "Recogido en " + Nombre(origen);
                case TiposMovimientoBulto.ENTREGADO: return almacen ? "Leído al recibir en " + Nombre(destino) + " (seguía en la furgoneta)" : "Entregado en " + Nombre(destino);
                case TiposMovimientoBulto.DESCARGADO: return almacen ? "Leído en el almacén (no se había descargado)" : "Descargado de la furgoneta";
                default: return null;
            }
        }

        /// <summary>A igual hora, el orden natural del viaje.</summary>
        private static int Orden(string tipo)
        {
            switch (tipo)
            {
                case EventoSeguimientoReposicionDTO.ETIQUETADO: return 0;
                case TiposMovimientoBulto.CARGADO: return 1;
                case TiposMovimientoBulto.RECOGIDO: return 1;
                case TiposMovimientoBulto.ENTREGADO: return 2;
                case TiposMovimientoBulto.DESCARGADO: return 2;
                default: return 3;
            }
        }

        internal static string EstadoBulto(List<EventoSeguimientoReposicionDTO> eventos, string origen, string destino)
        {
            EventoSeguimientoReposicionDTO ultimo = eventos.LastOrDefault();
            if (eventos.Any(e => e.Tipo == EventoSeguimientoReposicionDTO.RECIBIDO))
            {
                return "Recibido en " + Nombre(destino);
            }
            if (ultimo == null)
            {
                return "Sin etiquetar";
            }
            switch (ultimo.Tipo)
            {
                case TiposMovimientoBulto.CARGADO:
                case TiposMovimientoBulto.RECOGIDO:
                    return "En la furgoneta de " + (ultimo.Usuario ?? "un repartidor");
                case TiposMovimientoBulto.ENTREGADO: return "Entregado en " + Nombre(destino) + ", sin recibir";
                case TiposMovimientoBulto.DESCARGADO: return "Descargado en Algete, sin recibir";
                default: return "Preparado en " + Nombre(origen) + ", sin recoger";
            }
        }

        private static string Nombre(string almacen) => string.IsNullOrWhiteSpace(almacen) ? "¿?" : ServicioRepartoPropio.NombreTienda(almacen);
    }

    public class RepositorioSeguimientoReposicionesSql : IRepositorioSeguimientoReposiciones
    {
        private readonly NVEntities db;

        /// <param name="db">Null (los tests de los controladores): no hay nada.</param>
        public RepositorioSeguimientoReposicionesSql(NVEntities db)
        {
            this.db = db;
        }

        public bool FurgonetaDisponible() => db != null && ColumnasOpcionales.ExisteTabla(db, RepositorioFurgoneta.TABLA);

        public async Task<CabeceraSeguimientoReposicion> LeerCabecera(string empresa, int traspaso)
        {
            if (!ColumnasOpcionales.ExisteTabla(db, "ReposicionesTraspasos"))
            {
                return null;
            }
            bool conRecogida = ColumnasOpcionales.Existen(db, "ReposicionesTraspasos", "FechaRecogida", "UsuarioRecogida");
            string recogida = conRecogida ? "FechaRecogida, RTRIM(UsuarioRecogida) AS UsuarioRecogida" : "CAST(NULL AS datetime) AS FechaRecogida, CAST(NULL AS varchar(30)) AS UsuarioRecogida";
            return (await db.Database.SqlQuery<CabeceraSeguimientoReposicion>($@"
SELECT TOP 1 RTRIM(Origen) AS Origen, RTRIM(Destino) AS Destino, RTRIM(Herramienta) AS Herramienta, RTRIM(UsuarioCreacion) AS UsuarioCreacion,
       FechaCreacion, RTRIM(UsuarioPreparacion) AS UsuarioPreparacion, FechaPreparada, RTRIM(UsuarioRecepcion) AS UsuarioRecepcion, FechaRecibida, {recogida}
FROM dbo.ReposicionesTraspasos WHERE Empresa = @e AND NumTraspaso = @t",
                new SqlParameter("@e", SqlDbType.Char, 3) { Value = empresa }, new SqlParameter("@t", SqlDbType.Int) { Value = traspaso })
                .ToListAsync().ConfigureAwait(false)).FirstOrDefault();
        }

        public async Task<List<FilaBultoSeguimiento>> LeerBultos(string empresa, int traspaso)
        {
            if (!ColumnasOpcionales.ExisteTabla(db, "ReposicionesBultos"))
            {
                return null;
            }
            return await db.Database.SqlQuery<FilaBultoSeguimiento>(@"
SELECT RTRIM(Codigo) AS Codigo, CAST(Bulto AS int) AS Bulto, CAST(Total AS int) AS Total, FechaCreacion, RTRIM(UsuarioCreacion) AS UsuarioCreacion,
       FechaLectura, RTRIM(UsuarioLectura) AS UsuarioLectura
FROM dbo.ReposicionesBultos WHERE Empresa = @e AND NumTraspaso = @t ORDER BY Bulto",
                new SqlParameter("@e", SqlDbType.Char, 3) { Value = empresa }, new SqlParameter("@t", SqlDbType.Int) { Value = traspaso })
                .ToListAsync().ConfigureAwait(false);
        }

        public async Task<List<MovimientoBultoReparto>> LeerMovimientos(IReadOnlyCollection<int> traspasos)
        {
            if (traspasos == null || traspasos.Count == 0 || !FurgonetaDisponible())
            {
                return new List<MovimientoBultoReparto>();
            }
            return await new RepositorioFurgoneta(db).LeerHistoria(new ConsultaMovimientosBulto { Traspasos = traspasos }).ConfigureAwait(false);
        }

        public async Task<List<CargaAntiguaSeguimiento>> LeerCargasParadas(string empresa, int traspaso)
        {
            if (!ColumnasOpcionales.ExisteTabla(db, RepositorioParadasReposicion.TABLA) || !ColumnasOpcionales.ExisteTabla(db, RepositorioParadasReposicion.TABLA_BULTOS))
            {
                return new List<CargaAntiguaSeguimiento>();
            }
            return await db.Database.SqlQuery<CargaAntiguaSeguimiento>($@"
SELECT CAST(b.Bulto AS int) AS Bulto, RTRIM(b.Usuario) AS Usuario, b.FechaHora, b.ClaveIdempotencia, RTRIM(p.Destino) AS Destino,
       p.FechaEntrega, RTRIM(p.UsuarioEntrega) AS UsuarioEntrega, p.Receptor
FROM dbo.{RepositorioParadasReposicion.TABLA_BULTOS} b
JOIN dbo.{RepositorioParadasReposicion.TABLA} p ON p.Id = b.ParadaId
WHERE b.Empresa = @e AND b.NumTraspaso = @t ORDER BY b.Id",
                new SqlParameter("@e", SqlDbType.Char, 3) { Value = empresa }, new SqlParameter("@t", SqlDbType.Int) { Value = traspaso })
                .ToListAsync().ConfigureAwait(false);
        }

        public async Task<List<int>> TraspasosDelViaje(string empresa, string origen, string destino, DateTime diaCorte, DateTime hoy)
        {
            if (!ColumnasOpcionales.ExisteTabla(db, "ReposicionesTraspasos"))
            {
                return new List<int>();
            }
            return await db.Database.SqlQuery<int>(@"
SELECT NumTraspaso FROM dbo.ReposicionesTraspasos
WHERE Empresa = @e AND Origen = @o AND Destino = @d AND NumTraspaso IS NOT NULL AND Omitida IS NULL
  AND ((Herramienta = 'Automatico' AND FechaCorte >= @dia AND FechaCorte < DATEADD(day, 1, @dia))
    OR (Herramienta <> 'Automatico' AND FechaCreacion >= @hoy AND FechaCreacion < DATEADD(day, 1, @hoy)))",
                new SqlParameter("@e", SqlDbType.Char, 3) { Value = empresa }, new SqlParameter("@o", SqlDbType.Char, 3) { Value = origen },
                new SqlParameter("@d", SqlDbType.Char, 3) { Value = destino }, new SqlParameter("@dia", SqlDbType.DateTime) { Value = diaCorte.Date },
                new SqlParameter("@hoy", SqlDbType.DateTime) { Value = hoy.Date }).ToListAsync().ConfigureAwait(false);
        }

        public async Task<List<FilaBultoReposicion>> LeerBultosDe(string empresa, IReadOnlyCollection<int> traspasos)
        {
            if (traspasos == null || traspasos.Count == 0 || !ColumnasOpcionales.ExisteTabla(db, "ReposicionesBultos"))
            {
                return new List<FilaBultoReposicion>();
            }
            // Enteros en el texto (sin inyección posible)
            string lista = string.Join(", ", traspasos.Distinct().Select(t => t.ToString(CultureInfo.InvariantCulture)));
            return await db.Database.SqlQuery<FilaBultoReposicion>(
                $"SELECT {ReposicionesBultosSql.SQL_COLUMNAS} FROM dbo.ReposicionesBultos b WHERE b.Empresa = @e AND b.NumTraspaso IN ({lista})",
                new SqlParameter("@e", SqlDbType.Char, 3) { Value = empresa }).ToListAsync().ConfigureAwait(false);
        }
    }
}
