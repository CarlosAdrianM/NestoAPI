using NestoAPI.Infraestructure.Agencias.RepartoPropio;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Eventos
{
    // =====================================================================================================================
    // Eventos: candidatos a invitar, en MODO SOMBRA (corte 1, Carlos 11/11/26; NestoAPI#850).
    // Solo se recogen datos: por evento y cliente NV (sin vendedor) se guarda una foto de sus características y una puntuación por
    // reglas en dbo.EventosCandidatos (Scripts/EventosCandidatosSombra.sql), más una muestra aleatoria de control de los que no
    // pasan el corte. NADA es visible para usuarios ni clientes (solo un GET de resumen para Dirección/Informática). El resultado
    // (inscripción, asistencia) no se copia: lo cruza la vista dbo.vEventosCandidatosResultado.
    // =====================================================================================================================

    /// <summary>Lo que hace falta del evento para calcular sus candidatos.</summary>
    public class EventoParaCandidatos
    {
        public int Id { get; set; }
        public string Empresa { get; set; }
        public DateTime Fecha { get; set; }
        public string Almacen { get; set; }
        public bool Activo { get; set; }
        public string Formato { get; set; }
    }

    /// <summary>Una fila de la consulta de características (una por cliente NV contactable y no inscrito). Nombres = columnas.</summary>
    public class CaracteristicasCandidatoEvento
    {
        public string Cliente { get; set; }
        public int Contactos { get; set; }
        public DateTime? FechaAlta { get; set; }
        public bool TieneMovil { get; set; }
        public bool TieneCorreo { get; set; }
        public string CodigoPostal { get; set; }
        public string Direccion { get; set; }
        public string Poblacion { get; set; }
        public DateTime? UltimaCompraRequerido { get; set; }
        public decimal ImporteRequeridos24m { get; set; }
        public decimal ImporteMarca24m { get; set; }
        public int LineasMarca24m { get; set; }
        public DateTime? UltimaCompraMarca { get; set; }
        public int InscripcionesPrevias { get; set; }
        public int AsistenciasPrevias { get; set; }
        public int CursosComprados { get; set; }
        public DateTime? UltimoCurso { get; set; }
        public decimal Importe12m { get; set; }
        public decimal Importe24m { get; set; }
        public int Pedidos24m { get; set; }
        public DateTime? UltimaCompra { get; set; }
        public decimal ImporteTienda24m { get; set; }
    }

    /// <summary>Las características que puntúa la regla, ya en días y kilómetros (la foto que se guarda).</summary>
    public class FotoCandidatoEvento
    {
        public string Cliente { get; set; }
        public bool EsControl { get; set; }
        public decimal Puntuacion { get; set; }
        public int? DiasHastaEvento { get; set; }
        public decimal? DistanciaKm { get; set; }
        public string FuenteDistancia { get; set; }
        public string CodigoPostal { get; set; }
        public bool TieneMovil { get; set; }
        public bool TieneCorreo { get; set; }
        public int? AntiguedadDias { get; set; }
        public bool ComproRequerido { get; set; }
        public int? DiasUltimaCompraRequerido { get; set; }
        public decimal ImporteRequeridos24m { get; set; }
        public decimal ImporteMarca24m { get; set; }
        public int LineasMarca24m { get; set; }
        public int? DiasUltimaCompraMarca { get; set; }
        public int InscripcionesPrevias { get; set; }
        public int AsistenciasPrevias { get; set; }
        public int CursosComprados { get; set; }
        public int? DiasUltimoCurso { get; set; }
        public decimal Importe12m { get; set; }
        public decimal Importe24m { get; set; }
        public int Pedidos24m { get; set; }
        public int? DiasUltimaCompra { get; set; }
        public decimal ImporteTienda24m { get; set; }
        public int Contactos { get; set; }
        /// <summary>Presencial o mixto: la distancia cuenta. Online: no.</summary>
        public bool EventoPresencial { get; set; }
    }

    /// <summary>Resumen de un cálculo (lo que devuelve el servicio y el GET de administración).</summary>
    public class ResumenCandidatosEventoDTO
    {
        public int EventoId { get; set; }
        public DateTime? FechaCalculo { get; set; }
        public DateTime? FechaHora { get; set; }
        public string VersionRegla { get; set; }
        public int Candidatos { get; set; }
        public int Control { get; set; }
        public decimal? PuntuacionMaxima { get; set; }
        public decimal? PuntuacionMediaCandidatos { get; set; }
        public int ConDistancia { get; set; }
        /// <summary>Del cálculo: los clientes NV contactables y no inscritos que se han mirado; null en el GET.</summary>
        public int? Evaluados { get; set; }
        /// <summary>Del cálculo: por qué no se ha calculado (sin tablas, evento pasado o inactivo…); null si se ha calculado.</summary>
        public string Omitido { get; set; }
    }

    /// <summary>
    /// Las reglas de la puntuación (pura, sin BD). Versión «reglas-1»: puntos sumados y recortados a [0, 1]. Pesos razonados, no
    /// calibrados (no hay datos): el corte 2 los calibrará con dbo.vEventosCandidatosResultado y subirá la versión.
    /// </summary>
    public static class ReglaCandidatosEventos
    {
        public const string VERSION = "reglas-1";
        /// <summary>Desde esta puntuación, candidato; por debajo, solo puede entrar en la muestra de control.</summary>
        public const decimal UMBRAL = 0.30m;
        /// <summary>Como mucho, los mejores por evento y día (la lista corta será de unos 15; esto es para aprender).</summary>
        public const int MAXIMO_CANDIDATOS = 500;
        /// <summary>La muestra aleatoria de control (de los que no pasan el corte) por evento y día.</summary>
        public const int TAMANO_CONTROL = 300;

        public static decimal Puntuar(FotoCandidatoEvento f)
        {
            if (f == null)
            {
                return 0m;
            }
            decimal puntos = 0m;
            // Afinidad con lo del evento: haber comprado los productos asociados y la misma marca (familia, NO el subgrupo: quien
            // usa el equivalente de otra marca no es el público, Carlos 11/11/26)
            if (f.ComproRequerido)
            {
                puntos += 0.30m;
                if (f.DiasUltimaCompraRequerido.HasValue && f.DiasUltimaCompraRequerido.Value <= 365)
                {
                    puntos += 0.05m;
                }
            }
            if (f.ImporteMarca24m > 0m)
            {
                puntos += 0.10m + 0.10m * Math.Min(1m, f.ImporteMarca24m / 300m);
            }
            // Ya ha venido a eventos o ha hecho cursos
            if (f.InscripcionesPrevias > 0)
            {
                puntos += 0.15m;
            }
            if (f.AsistenciasPrevias > 0)
            {
                puntos += 0.05m;
            }
            if (f.CursosComprados > 0)
            {
                puntos += 0.10m;
            }
            // Cerca de la tienda (solo presencial o mixto)
            if (f.EventoPresencial && f.DistanciaKm.HasValue)
            {
                decimal km = f.DistanciaKm.Value;
                puntos += km <= 10m ? 0.15m : km <= 25m ? 0.10m : km <= 50m ? 0.05m : km > 100m ? -0.10m : 0m;
            }
            // Cliente vivo y de esa tienda
            if (f.DiasUltimaCompra.HasValue)
            {
                puntos += f.DiasUltimaCompra.Value <= 90 ? 0.10m : f.DiasUltimaCompra.Value <= 365 ? 0.05m : 0m;
            }
            if (f.ImporteTienda24m > 0m)
            {
                puntos += 0.10m;
            }
            // Se le puede escribir por WhatsApp
            if (f.TieneMovil)
            {
                puntos += 0.05m;
            }
            return Math.Round(Math.Max(0m, Math.Min(1m, puntos)), 4);
        }

        /// <summary>Distancia en línea recta (haversine, radio medio de la Tierra 6371 km), en km con 2 decimales.</summary>
        public static decimal DistanciaKm(CoordenadasGeo a, CoordenadasGeo b)
        {
            if (a == null || b == null)
            {
                throw new ArgumentNullException(a == null ? nameof(a) : nameof(b));
            }
            const double RADIO = 6371.0;
            double lat1 = Radianes((double)a.Latitud), lat2 = Radianes((double)b.Latitud);
            double dLat = lat2 - lat1, dLon = Radianes((double)b.Longitud - (double)a.Longitud);
            double h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Cos(lat1) * Math.Cos(lat2) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            double km = 2 * RADIO * Math.Asin(Math.Min(1.0, Math.Sqrt(h)));
            return Math.Round((decimal)km, 2);
        }

        private static double Radianes(double grados) => grados * Math.PI / 180.0;

        /// <summary>Los mejores que pasan el corte (como mucho <see cref="MAXIMO_CANDIDATOS"/>) y la muestra de control de los que no.</summary>
        public static List<FotoCandidatoEvento> Seleccionar(IEnumerable<FotoCandidatoEvento> puntuados, int semilla)
        {
            List<FotoCandidatoEvento> todos = (puntuados ?? Enumerable.Empty<FotoCandidatoEvento>()).Where(f => f != null).ToList();
            List<FotoCandidatoEvento> candidatos = todos.Where(f => f.Puntuacion >= UMBRAL)
                .OrderByDescending(f => f.Puntuacion).ThenBy(f => f.Cliente, StringComparer.Ordinal)
                .Take(MAXIMO_CANDIDATOS).ToList();
            foreach (FotoCandidatoEvento f in candidatos)
            {
                f.EsControl = false;
            }
            // Control: al azar (semilla fija por evento y día: relanzar el mismo día elige los mismos) entre los que no pasan el corte
            var aleatorio = new Random(semilla);
            List<FotoCandidatoEvento> control = todos.Where(f => f.Puntuacion < UMBRAL)
                .OrderBy(f => f.Cliente, StringComparer.Ordinal)
                .Select(f => new { Foto = f, Orden = aleatorio.Next() })
                .OrderBy(x => x.Orden)
                .Take(TAMANO_CONTROL)
                .Select(x => x.Foto)
                .ToList();
            foreach (FotoCandidatoEvento f in control)
            {
                f.EsControl = true;
            }
            return candidatos.Concat(control).ToList();
        }

        /// <summary>La semilla del control: la misma para el mismo evento y día.</summary>
        public static int Semilla(int eventoId, DateTime dia) => unchecked(eventoId * 100003 + (int)(dia.Date - new DateTime(2000, 1, 1)).TotalDays);
    }

    public interface IRepositorioCandidatosEventos
    {
        /// <summary>Están dbo.EventosCandidatos (sin ella no se calcula nada).</summary>
        Task<bool> HayTabla();
        Task<EventoParaCandidatos> LeerEvento(int id);
        /// <summary>Los eventos activos de hoy en adelante.</summary>
        Task<List<int>> EventosFuturosActivos(DateTime hoy);
        Task<List<CaracteristicasCandidatoEvento>> LeerCaracteristicas(EventoParaCandidatos evento, DateTime hoy);
        /// <summary>La caché de direcciones geocodificadas del reparto (dbo.DireccionesCoordenadas), por clave. Sin tabla, vacía.</summary>
        Task<Dictionary<string, CoordenadasGeo>> CoordenadasDirecciones();
        /// <summary>Los centros de los códigos postales (dbo.CentrosCodigosPostales), por CP. Sin tabla o sin filas, vacía.</summary>
        Task<Dictionary<string, CoordenadasGeo>> CentrosCodigosPostales();
        /// <summary>Sustituye la foto de ese evento y día (idempotente por evento + cliente + día). Devuelve las filas guardadas.</summary>
        Task<int> Guardar(EventoParaCandidatos evento, DateTime dia, string motivo, List<FotoCandidatoEvento> fotos);
        /// <summary>El último cálculo de cada evento.</summary>
        Task<List<ResumenCandidatosEventoDTO>> Resumen();
    }

    /// <summary>Calcula, puntúa y guarda la foto de los candidatos de un evento. Sin las tablas, no hace nada.</summary>
    public class ServicioCandidatosEventos
    {
        public const string MOTIVO_ALTA = "Alta";
        public const string MOTIVO_ANUNCIO = "Anuncio";
        public const string MOTIVO_DIARIO = "Diario";

        private readonly IRepositorioCandidatosEventos repositorio;

        public ServicioCandidatosEventos(IRepositorioCandidatosEventos repositorio)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
        }

        public async Task<ResumenCandidatosEventoDTO> Calcular(int eventoId, DateTime hoy, string motivo)
        {
            hoy = hoy.Date;
            var resumen = new ResumenCandidatosEventoDTO { EventoId = eventoId, FechaCalculo = hoy, VersionRegla = ReglaCandidatosEventos.VERSION };
            if (!await repositorio.HayTabla().ConfigureAwait(false))
            {
                resumen.Omitido = "Falta lanzar Scripts/EventosCandidatosSombra.sql.";
                return resumen;
            }
            EventoParaCandidatos evento = await repositorio.LeerEvento(eventoId).ConfigureAwait(false);
            if (evento == null || !evento.Activo || evento.Fecha.Date < hoy)
            {
                resumen.Omitido = evento == null ? "El evento no existe." : !evento.Activo ? "El evento no está activo." : "El evento ya ha pasado.";
                return resumen;
            }

            List<CaracteristicasCandidatoEvento> filas = await repositorio.LeerCaracteristicas(evento, hoy).ConfigureAwait(false)
                ?? new List<CaracteristicasCandidatoEvento>();
            CoordenadasGeo tienda = SedesReparto.DeAlmacen(evento.Almacen);
            Dictionary<string, CoordenadasGeo> direcciones = tienda == null ? new Dictionary<string, CoordenadasGeo>()
                : await repositorio.CoordenadasDirecciones().ConfigureAwait(false) ?? new Dictionary<string, CoordenadasGeo>();
            Dictionary<string, CoordenadasGeo> centros = tienda == null ? new Dictionary<string, CoordenadasGeo>()
                : await repositorio.CentrosCodigosPostales().ConfigureAwait(false) ?? new Dictionary<string, CoordenadasGeo>();
            bool presencial = !string.Equals(evento.Formato?.Trim(), FormatosEvento.ONLINE, StringComparison.OrdinalIgnoreCase);

            List<FotoCandidatoEvento> fotos = filas.Where(f => f != null && !string.IsNullOrWhiteSpace(f.Cliente))
                .Select(f => Foto(f, evento, hoy, tienda, direcciones, centros, presencial))
                .ToList();
            List<FotoCandidatoEvento> elegidos = ReglaCandidatosEventos.Seleccionar(fotos, ReglaCandidatosEventos.Semilla(eventoId, hoy));
            _ = await repositorio.Guardar(evento, hoy, motivo, elegidos).ConfigureAwait(false);

            resumen.Evaluados = fotos.Count;
            resumen.Candidatos = elegidos.Count(f => !f.EsControl);
            resumen.Control = elegidos.Count(f => f.EsControl);
            resumen.ConDistancia = elegidos.Count(f => f.DistanciaKm.HasValue);
            resumen.PuntuacionMaxima = elegidos.Any() ? elegidos.Max(f => f.Puntuacion) : (decimal?)null;
            resumen.PuntuacionMediaCandidatos = resumen.Candidatos > 0 ? Math.Round(elegidos.Where(f => !f.EsControl).Average(f => f.Puntuacion), 4) : (decimal?)null;
            return resumen;
        }

        internal static FotoCandidatoEvento Foto(CaracteristicasCandidatoEvento f, EventoParaCandidatos evento, DateTime hoy, CoordenadasGeo tienda,
            IDictionary<string, CoordenadasGeo> direcciones, IDictionary<string, CoordenadasGeo> centros, bool presencial)
        {
            int? Dias(DateTime? fecha) => fecha.HasValue ? (int)(hoy - fecha.Value.Date).TotalDays : (int?)null;
            string cp = f.CodigoPostal?.Trim();
            var foto = new FotoCandidatoEvento
            {
                Cliente = f.Cliente.Trim(),
                DiasHastaEvento = (int)(evento.Fecha.Date - hoy).TotalDays,
                CodigoPostal = string.IsNullOrEmpty(cp) ? null : cp,
                TieneMovil = f.TieneMovil,
                TieneCorreo = f.TieneCorreo,
                AntiguedadDias = Dias(f.FechaAlta),
                ComproRequerido = f.UltimaCompraRequerido.HasValue,
                DiasUltimaCompraRequerido = Dias(f.UltimaCompraRequerido),
                ImporteRequeridos24m = f.ImporteRequeridos24m,
                ImporteMarca24m = f.ImporteMarca24m,
                LineasMarca24m = f.LineasMarca24m,
                DiasUltimaCompraMarca = Dias(f.UltimaCompraMarca),
                InscripcionesPrevias = f.InscripcionesPrevias,
                AsistenciasPrevias = f.AsistenciasPrevias,
                CursosComprados = f.CursosComprados,
                DiasUltimoCurso = Dias(f.UltimoCurso),
                Importe12m = f.Importe12m,
                Importe24m = f.Importe24m,
                Pedidos24m = f.Pedidos24m,
                DiasUltimaCompra = Dias(f.UltimaCompra),
                ImporteTienda24m = f.ImporteTienda24m,
                Contactos = f.Contactos,
                EventoPresencial = presencial
            };
            if (tienda != null)
            {
                // Con preferencia, la dirección geocodificada del reparto; si no, el centro de su código postal
                string clave = new DireccionParada(f.Direccion, f.CodigoPostal, f.Poblacion).Clave;
                if (direcciones != null && direcciones.TryGetValue(clave, out CoordenadasGeo punto) && punto != null)
                {
                    foto.DistanciaKm = ReglaCandidatosEventos.DistanciaKm(punto, tienda);
                    foto.FuenteDistancia = "Direccion";
                }
                else if (cp != null && cp.Length >= 5 && centros != null && centros.TryGetValue(cp.Substring(0, 5), out CoordenadasGeo centro) && centro != null)
                {
                    foto.DistanciaKm = ReglaCandidatosEventos.DistanciaKm(centro, tienda);
                    foto.FuenteDistancia = "CodigoPostal";
                }
            }
            foto.Puntuacion = ReglaCandidatosEventos.Puntuar(foto);
            return foto;
        }
    }

    /// <summary>
    /// Las tablas de la sombra van por SQL, fuera del EDMX (como DiasRepartoClientes o CalendarioFestivos): sin tabla, sin función.
    /// Una consulta de características por evento (tablas temporales y agregados, sin N+1); la tabla Clientes solo se LEE (tiene
    /// un disparador pesado). Se escribe con SqlBulkCopy en una transacción (borra la foto del día y la vuelve a poner).
    /// </summary>
    public class RepositorioCandidatosEventosSql : IRepositorioCandidatosEventos
    {
        public const string TABLA = "EventosCandidatos";
        public const string TABLA_CENTROS = "CentrosCodigosPostales";
        private const string EMPRESA = Constantes.Empresas.EMPRESA_POR_DEFECTO;

        private readonly Func<NVEntities> crearContexto;

        public RepositorioCandidatosEventosSql(Func<NVEntities> crearContexto = null)
        {
            this.crearContexto = crearContexto ?? (() =>
            {
                var db = new NVEntities();
                db.Configuration.LazyLoadingEnabled = false;
                db.Configuration.ProxyCreationEnabled = false;
                return db;
            });
        }

        private async Task<bool> ExisteTabla(string tabla)
        {
            using (NVEntities db = crearContexto())
            {
                List<int> filas = await db.Database.SqlQuery<int>(
                    $"SELECT CASE WHEN OBJECT_ID(N'dbo.{tabla}', N'U') IS NULL THEN 0 ELSE 1 END").ToListAsync().ConfigureAwait(false);
                return filas.FirstOrDefault() == 1;
            }
        }

        public Task<bool> HayTabla() => ExisteTabla(TABLA);

        public async Task<EventoParaCandidatos> LeerEvento(int id)
        {
            using (NVEntities db = crearContexto())
            {
                List<EventoParaCandidatos> filas = await db.Database.SqlQuery<EventoParaCandidatos>(
                    "SELECT Id, RTRIM(Empresa) AS Empresa, CAST(Fecha AS datetime) AS Fecha, RTRIM(Almacen) AS Almacen, CAST(Activo AS bit) AS Activo, " +
                    "RTRIM(Formato) AS Formato FROM dbo.Eventos WITH (NOLOCK) WHERE Id = @id",
                    new SqlParameter("@id", SqlDbType.Int) { Value = id }).ToListAsync().ConfigureAwait(false);
                return filas.FirstOrDefault();
            }
        }

        public async Task<List<int>> EventosFuturosActivos(DateTime hoy)
        {
            using (NVEntities db = crearContexto())
            {
                return await db.Database.SqlQuery<int>(
                    "SELECT Id FROM dbo.Eventos WITH (NOLOCK) WHERE Activo = 1 AND Fecha >= @hoy ORDER BY Fecha, Id",
                    new SqlParameter("@hoy", SqlDbType.Date) { Value = hoy.Date }).ToListAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Candidatos: clientes con todos sus contactos activos de la empresa 1 del vendedor general (NV) y sin otro vendedor por grupo
        /// de producto (como el embudo), que no estén ya inscritos en el evento, que admitan comunicaciones (si tienen personas de
        /// contacto activas, alguna con EnviarBoletin, como los leads sin consentimiento) y con móvil o correo.
        /// </summary>
        internal const string SQL_CARACTERISTICAS = @"
SET NOCOUNT ON;
IF OBJECT_ID(N'tempdb..#CandEv') IS NOT NULL DROP TABLE #CandEv;
IF OBJECT_ID(N'tempdb..#ReqEv') IS NOT NULL DROP TABLE #ReqEv;
IF OBJECT_ID(N'tempdb..#FamEv') IS NOT NULL DROP TABLE #FamEv;
IF OBJECT_ID(N'tempdb..#VentasEv') IS NOT NULL DROP TABLE #VentasEv;
IF OBJECT_ID(N'tempdb..#AggEv') IS NOT NULL DROP TABLE #AggEv;
IF OBJECT_ID(N'tempdb..#ReqHistEv') IS NOT NULL DROP TABLE #ReqHistEv;
IF OBJECT_ID(N'tempdb..#CurHistEv') IS NOT NULL DROP TABLE #CurHistEv;
IF OBJECT_ID(N'tempdb..#InsEv') IS NOT NULL DROP TABLE #InsEv;

SELECT c.[Nº Cliente] AS Cliente, MIN(c.FechaAlta) AS FechaAlta, COUNT(*) AS Contactos,
       MAX(CASE WHEN c.Teléfono LIKE '%[67][0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9]%' THEN 1 ELSE 0 END) AS MovilFicha
INTO #CandEv
FROM Clientes c WITH (NOLOCK)
WHERE c.Empresa = @empresa AND c.Estado >= 0
GROUP BY c.[Nº Cliente]
HAVING SUM(CASE WHEN c.Vendedor = @vendedor THEN 0 ELSE 1 END) = 0;
CREATE CLUSTERED INDEX IX_CandEv ON #CandEv (Cliente);

DELETE a FROM #CandEv a
WHERE EXISTS (SELECT 1 FROM VendedoresClienteGrupoProducto v WITH (NOLOCK)
              INNER JOIN Clientes c WITH (NOLOCK) ON c.Empresa = v.Empresa AND c.[Nº Cliente] = v.Cliente AND c.Contacto = v.Contacto
              WHERE v.Empresa = @empresa AND v.Cliente = a.Cliente AND c.Estado >= 0 AND v.Vendedor <> @vendedor);

DELETE a FROM #CandEv a
WHERE EXISTS (SELECT 1 FROM EventosInscripciones i WITH (NOLOCK)
              WHERE i.EventoId = @evento AND i.Empresa = @empresa AND i.Cliente = a.Cliente AND i.Estado NOT IN ('Anulada', 'Caducada'));

DELETE a FROM #CandEv a
WHERE EXISTS (SELECT 1 FROM PersonasContactoCliente p WITH (NOLOCK) WHERE p.Empresa = @empresa AND p.[NºCliente] = a.Cliente AND p.Estado >= 0)
  AND NOT EXISTS (SELECT 1 FROM PersonasContactoCliente p WITH (NOLOCK) WHERE p.Empresa = @empresa AND p.[NºCliente] = a.Cliente AND p.Estado >= 0 AND p.EnviarBoletin = 1);

SELECT DISTINCT r.Producto INTO #ReqEv FROM EventosProductosRequeridos r WITH (NOLOCK) WHERE r.EventoId = @evento;
SELECT DISTINCT p.Familia INTO #FamEv FROM #ReqEv r INNER JOIN Productos p WITH (NOLOCK) ON p.Empresa = @empresa AND p.Número = r.Producto
WHERE p.Familia IS NOT NULL AND RTRIM(p.Familia) <> '';

SELECT l.[Nº Cliente] AS Cliente, l.Número AS Pedido, l.Producto, l.Familia, l.Almacén AS Almacen,
       CAST(ISNULL(l.[Base Imponible], 0) AS decimal(14, 4)) AS Base, l.[Fecha Albarán] AS Fecha
INTO #VentasEv
FROM LinPedidoVta l WITH (NOLOCK)
INNER JOIN #CandEv a ON a.Cliente = l.[Nº Cliente]
WHERE l.Empresa IN (@empresa, @empresaEspejo) AND l.Estado >= @albaran AND l.[Fecha Albarán] >= @desde24;
CREATE CLUSTERED INDEX IX_VentasEv ON #VentasEv (Cliente);

SELECT v.Cliente,
       SUM(CASE WHEN v.Fecha >= @desde12 THEN v.Base ELSE 0 END) AS Importe12m,
       SUM(v.Base) AS Importe24m,
       COUNT(DISTINCT v.Pedido) AS Pedidos24m,
       MAX(v.Fecha) AS UltimaCompra,
       SUM(CASE WHEN v.Almacen = @almacen THEN v.Base ELSE 0 END) AS ImporteTienda24m,
       SUM(CASE WHEN f.Familia IS NOT NULL THEN v.Base ELSE 0 END) AS ImporteMarca24m,
       SUM(CASE WHEN f.Familia IS NOT NULL THEN 1 ELSE 0 END) AS LineasMarca24m,
       MAX(CASE WHEN f.Familia IS NOT NULL THEN v.Fecha END) AS UltimaCompraMarca,
       SUM(CASE WHEN r.Producto IS NOT NULL THEN v.Base ELSE 0 END) AS ImporteRequeridos24m
INTO #AggEv
FROM #VentasEv v
LEFT JOIN #FamEv f ON f.Familia = v.Familia
LEFT JOIN #ReqEv r ON r.Producto = v.Producto
GROUP BY v.Cliente;

SELECT l.[Nº Cliente] AS Cliente, MAX(l.[Fecha Albarán]) AS UltimaCompraRequerido
INTO #ReqHistEv
FROM LinPedidoVta l WITH (NOLOCK)
INNER JOIN #ReqEv r ON r.Producto = l.Producto
INNER JOIN #CandEv a ON a.Cliente = l.[Nº Cliente]
WHERE l.Empresa IN (@empresa, @empresaEspejo) AND l.Estado >= @albaran
GROUP BY l.[Nº Cliente];

SELECT l.[Nº Cliente] AS Cliente, COUNT(*) AS CursosComprados, MAX(l.[Fecha Albarán]) AS UltimoCurso
INTO #CurHistEv
FROM LinPedidoVta l WITH (NOLOCK)
INNER JOIN #CandEv a ON a.Cliente = l.[Nº Cliente]
WHERE l.Empresa IN (@empresa, @empresaEspejo) AND l.Estado >= @albaran AND l.Grupo = @grupoCursos
GROUP BY l.[Nº Cliente];

SELECT i.Cliente,
       SUM(CASE WHEN i.Estado NOT IN ('Anulada', 'Caducada') THEN 1 ELSE 0 END) AS InscripcionesPrevias,
       SUM(CASE WHEN i.Estado = 'Asistio' THEN 1 ELSE 0 END) AS AsistenciasPrevias
INTO #InsEv
FROM EventosInscripciones i WITH (NOLOCK)
INNER JOIN #CandEv a ON a.Cliente = i.Cliente
WHERE i.Empresa = @empresa AND i.EventoId <> @evento
GROUP BY i.Cliente;

SELECT t.* FROM (
SELECT RTRIM(a.Cliente) AS Cliente, a.Contactos, a.FechaAlta,
       CAST(CASE WHEN a.MovilFicha = 1 OR EXISTS (SELECT 1 FROM PersonasContactoCliente p WITH (NOLOCK)
            WHERE p.Empresa = @empresa AND p.[NºCliente] = a.Cliente AND p.Estado >= 0
              AND p.Teléfono LIKE '%[67][0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9]%') THEN 1 ELSE 0 END AS bit) AS TieneMovil,
       CAST(CASE WHEN EXISTS (SELECT 1 FROM PersonasContactoCliente p WITH (NOLOCK)
            WHERE p.Empresa = @empresa AND p.[NºCliente] = a.Cliente AND p.Estado >= 0
              AND p.CorreoElectrónico LIKE '%@%' AND p.EnviarBoletin = 1) THEN 1 ELSE 0 END AS bit) AS TieneCorreo,
       RTRIM(dir.CodPostal) AS CodigoPostal, RTRIM(dir.Direccion) AS Direccion, RTRIM(dir.Poblacion) AS Poblacion,
       rh.UltimaCompraRequerido,
       CAST(ISNULL(g.ImporteRequeridos24m, 0) AS decimal(12, 2)) AS ImporteRequeridos24m,
       CAST(ISNULL(g.ImporteMarca24m, 0) AS decimal(12, 2)) AS ImporteMarca24m,
       ISNULL(g.LineasMarca24m, 0) AS LineasMarca24m,
       g.UltimaCompraMarca,
       ISNULL(ins.InscripcionesPrevias, 0) AS InscripcionesPrevias,
       ISNULL(ins.AsistenciasPrevias, 0) AS AsistenciasPrevias,
       ISNULL(cu.CursosComprados, 0) AS CursosComprados,
       cu.UltimoCurso,
       CAST(ISNULL(g.Importe12m, 0) AS decimal(12, 2)) AS Importe12m,
       CAST(ISNULL(g.Importe24m, 0) AS decimal(12, 2)) AS Importe24m,
       ISNULL(g.Pedidos24m, 0) AS Pedidos24m,
       g.UltimaCompra,
       CAST(ISNULL(g.ImporteTienda24m, 0) AS decimal(12, 2)) AS ImporteTienda24m
FROM #CandEv a
LEFT JOIN #AggEv g ON g.Cliente = a.Cliente
LEFT JOIN #ReqHistEv rh ON rh.Cliente = a.Cliente
LEFT JOIN #CurHistEv cu ON cu.Cliente = a.Cliente
LEFT JOIN #InsEv ins ON ins.Cliente = a.Cliente
OUTER APPLY (SELECT TOP 1 c.CodPostal, c.Dirección AS Direccion, c.Población AS Poblacion
             FROM Clientes c WITH (NOLOCK)
             WHERE c.Empresa = @empresa AND c.[Nº Cliente] = a.Cliente AND c.Estado >= 0
             ORDER BY CASE WHEN c.ClientePrincipal = 1 THEN 0 ELSE 1 END, c.Contacto) dir
) t
WHERE t.TieneMovil = 1 OR t.TieneCorreo = 1;

DROP TABLE #CandEv; DROP TABLE #ReqEv; DROP TABLE #FamEv; DROP TABLE #VentasEv; DROP TABLE #AggEv;
DROP TABLE #ReqHistEv; DROP TABLE #CurHistEv; DROP TABLE #InsEv;";

        public async Task<List<CaracteristicasCandidatoEvento>> LeerCaracteristicas(EventoParaCandidatos evento, DateTime hoy)
        {
            using (NVEntities db = crearContexto())
            {
                // Lee mucho (de madrugada o al crear un evento): más margen que los 30 s de siempre
                db.Database.CommandTimeout = 180;
                return await db.Database.SqlQuery<CaracteristicasCandidatoEvento>(SQL_CARACTERISTICAS,
                    new SqlParameter("@empresa", SqlDbType.Char, 3) { Value = string.IsNullOrWhiteSpace(evento.Empresa) ? EMPRESA : evento.Empresa.Trim() },
                    new SqlParameter("@empresaEspejo", SqlDbType.Char, 3) { Value = Constantes.Empresas.EMPRESA_ESPEJO_POR_DEFECTO },
                    new SqlParameter("@vendedor", SqlDbType.Char, 3) { Value = Constantes.Vendedores.VENDEDOR_GENERAL },
                    new SqlParameter("@evento", SqlDbType.Int) { Value = evento.Id },
                    new SqlParameter("@almacen", SqlDbType.Char, 3) { Value = (object)evento.Almacen?.Trim() ?? DBNull.Value },
                    new SqlParameter("@albaran", SqlDbType.SmallInt) { Value = Constantes.EstadosLineaVenta.ALBARAN },
                    new SqlParameter("@grupoCursos", SqlDbType.Char, 3) { Value = Constantes.Productos.GRUPO_CURSOS },
                    new SqlParameter("@desde12", SqlDbType.DateTime) { Value = hoy.Date.AddMonths(-12) },
                    new SqlParameter("@desde24", SqlDbType.DateTime) { Value = hoy.Date.AddMonths(-24) })
                    .ToListAsync().ConfigureAwait(false);
            }
        }

        private class FilaCoordenadas
        {
            public string Clave { get; set; }
            public decimal? Latitud { get; set; }
            public decimal? Longitud { get; set; }
        }

        private async Task<Dictionary<string, CoordenadasGeo>> LeerCoordenadas(string tabla, string sql)
        {
            var resultado = new Dictionary<string, CoordenadasGeo>(StringComparer.OrdinalIgnoreCase);
            if (!await ExisteTabla(tabla).ConfigureAwait(false))
            {
                return resultado;
            }
            using (NVEntities db = crearContexto())
            {
                List<FilaCoordenadas> filas = await db.Database.SqlQuery<FilaCoordenadas>(sql).ToListAsync().ConfigureAwait(false);
                foreach (FilaCoordenadas fila in filas.Where(f => !string.IsNullOrWhiteSpace(f.Clave) && CoordenadasGeo.Validas(f.Latitud, f.Longitud)))
                {
                    resultado[fila.Clave.Trim()] = new CoordenadasGeo(fila.Latitud.Value, fila.Longitud.Value);
                }
            }
            return resultado;
        }

        // Las dos son pequeñas (la caché del reparto: cientos; los CP de España: ~11.000): se leen enteras una vez por cálculo
        public Task<Dictionary<string, CoordenadasGeo>> CoordenadasDirecciones() => LeerCoordenadas(RepositorioCoordenadas.TABLA,
            $"SELECT RTRIM(Clave) AS Clave, Latitud, Longitud FROM dbo.{RepositorioCoordenadas.TABLA} WITH (NOLOCK)");

        public Task<Dictionary<string, CoordenadasGeo>> CentrosCodigosPostales() => LeerCoordenadas(TABLA_CENTROS,
            $"SELECT RTRIM(CodigoPostal) AS Clave, Latitud, Longitud FROM dbo.{TABLA_CENTROS} WITH (NOLOCK)");

        public async Task<int> Guardar(EventoParaCandidatos evento, DateTime dia, string motivo, List<FotoCandidatoEvento> fotos)
        {
            fotos = fotos ?? new List<FotoCandidatoEvento>();
            string empresa = string.IsNullOrWhiteSpace(evento.Empresa) ? EMPRESA : evento.Empresa.Trim();
            DataTable tabla = TablaFotos(evento.Id, empresa, dia.Date, motivo, fotos);
            using (NVEntities db = crearContexto())
            {
                var conexion = (SqlConnection)db.Database.Connection;
                await conexion.OpenAsync().ConfigureAwait(false);
                using (SqlTransaction transaccion = conexion.BeginTransaction())
                {
                    using (var borrar = new SqlCommand($"DELETE FROM dbo.{TABLA} WHERE EventoId = @evento AND Empresa = @empresa AND FechaCalculo = @dia", conexion, transaccion))
                    {
                        _ = borrar.Parameters.Add(new SqlParameter("@evento", SqlDbType.Int) { Value = evento.Id });
                        _ = borrar.Parameters.Add(new SqlParameter("@empresa", SqlDbType.Char, 3) { Value = empresa });
                        _ = borrar.Parameters.Add(new SqlParameter("@dia", SqlDbType.Date) { Value = dia.Date });
                        _ = await borrar.ExecuteNonQueryAsync().ConfigureAwait(false);
                    }
                    using (var copia = new SqlBulkCopy(conexion, SqlBulkCopyOptions.Default, transaccion) { DestinationTableName = "dbo." + TABLA })
                    {
                        foreach (DataColumn columna in tabla.Columns)
                        {
                            _ = copia.ColumnMappings.Add(columna.ColumnName, columna.ColumnName);
                        }
                        await copia.WriteToServerAsync(tabla).ConfigureAwait(false);
                    }
                    transaccion.Commit();
                }
            }
            return fotos.Count;
        }

        internal static DataTable TablaFotos(int eventoId, string empresa, DateTime dia, string motivo, IEnumerable<FotoCandidatoEvento> fotos)
        {
            var t = new DataTable();
            void Columna<T>(string nombre) => t.Columns.Add(nombre, Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T));
            Columna<int>("EventoId"); Columna<string>("Empresa"); Columna<string>("Cliente"); Columna<DateTime>("FechaCalculo");
            Columna<DateTime>("FechaHora"); Columna<string>("Motivo"); Columna<bool>("EsControl"); Columna<string>("VersionRegla");
            Columna<decimal>("Puntuacion"); Columna<int?>("DiasHastaEvento"); Columna<decimal?>("DistanciaKm"); Columna<string>("FuenteDistancia");
            Columna<string>("CodigoPostal"); Columna<bool>("TieneMovil"); Columna<bool>("TieneCorreo"); Columna<int?>("AntiguedadDias");
            Columna<bool>("ComproRequerido"); Columna<int?>("DiasUltimaCompraRequerido"); Columna<decimal>("ImporteRequeridos24m");
            Columna<decimal>("ImporteMarca24m"); Columna<int>("LineasMarca24m"); Columna<int?>("DiasUltimaCompraMarca");
            Columna<int>("InscripcionesPrevias"); Columna<int>("AsistenciasPrevias"); Columna<int>("CursosComprados"); Columna<int?>("DiasUltimoCurso");
            Columna<decimal>("Importe12m"); Columna<decimal>("Importe24m"); Columna<int>("Pedidos24m"); Columna<int?>("DiasUltimaCompra");
            Columna<decimal>("ImporteTienda24m"); Columna<int>("Contactos");
            DateTime ahora = DateTime.Now;
            object N(object valor) => valor ?? DBNull.Value;
            string Recortar(string s, int largo) => s == null ? null : s.Length <= largo ? s : s.Substring(0, largo);
            foreach (FotoCandidatoEvento f in fotos)
            {
                _ = t.Rows.Add(eventoId, empresa, Recortar(f.Cliente, 10), dia, ahora, Recortar(motivo ?? ServicioCandidatosEventos.MOTIVO_DIARIO, 20), f.EsControl,
                    ReglaCandidatosEventos.VERSION, f.Puntuacion, N(f.DiasHastaEvento), N(f.DistanciaKm), N(f.FuenteDistancia), N(Recortar(f.CodigoPostal, 10)),
                    f.TieneMovil, f.TieneCorreo, N(f.AntiguedadDias), f.ComproRequerido, N(f.DiasUltimaCompraRequerido), f.ImporteRequeridos24m,
                    f.ImporteMarca24m, f.LineasMarca24m, N(f.DiasUltimaCompraMarca), f.InscripcionesPrevias, f.AsistenciasPrevias, f.CursosComprados,
                    N(f.DiasUltimoCurso), f.Importe12m, f.Importe24m, f.Pedidos24m, N(f.DiasUltimaCompra), f.ImporteTienda24m, f.Contactos);
            }
            return t;
        }

        internal const string SQL_RESUMEN = @"
SELECT c.EventoId, CAST(c.FechaCalculo AS datetime) AS FechaCalculo, MAX(c.FechaHora) AS FechaHora, MAX(c.VersionRegla) AS VersionRegla,
       SUM(CASE WHEN c.EsControl = 0 THEN 1 ELSE 0 END) AS Candidatos,
       SUM(CASE WHEN c.EsControl = 1 THEN 1 ELSE 0 END) AS Control,
       MAX(c.Puntuacion) AS PuntuacionMaxima,
       CAST(AVG(CASE WHEN c.EsControl = 0 THEN c.Puntuacion END) AS decimal(9, 4)) AS PuntuacionMediaCandidatos,
       SUM(CASE WHEN c.DistanciaKm IS NOT NULL THEN 1 ELSE 0 END) AS ConDistancia
FROM dbo.EventosCandidatos c WITH (NOLOCK)
INNER JOIN (SELECT EventoId, MAX(FechaCalculo) AS Ultima FROM dbo.EventosCandidatos WITH (NOLOCK) GROUP BY EventoId) u
        ON u.EventoId = c.EventoId AND u.Ultima = c.FechaCalculo
GROUP BY c.EventoId, c.FechaCalculo
ORDER BY c.EventoId";

        public async Task<List<ResumenCandidatosEventoDTO>> Resumen()
        {
            if (!await HayTabla().ConfigureAwait(false))
            {
                return new List<ResumenCandidatosEventoDTO>();
            }
            using (NVEntities db = crearContexto())
            {
                return await db.Database.SqlQuery<ResumenCandidatosEventoDTO>(SQL_RESUMEN).ToListAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Hangfire: «eventos-candidatos-sombra» de madrugada (fuera de horario) vuelve a fotografiar los eventos futuros activos (así se
    /// ve cómo cambia con el tiempo), y <see cref="CalcularEvento"/> al crear o anunciar un evento. Nunca lanza (lo deja en ELMAH).
    /// </summary>
    public class CandidatosEventosJobsService
    {
        public const string JOB_ID = "eventos-candidatos-sombra";
        /// <summary>Todos los días a las 3:15.</summary>
        public const string CRON = "15 3 * * *";

        private readonly ServicioCandidatosEventos servicio;
        private readonly IRepositorioCandidatosEventos repositorio;
        private readonly Action<Exception> registrar;

        public CandidatosEventosJobsService() : this(new RepositorioCandidatosEventosSql())
        {
        }

        internal CandidatosEventosJobsService(IRepositorioCandidatosEventos repositorio, Action<Exception> registrar = null)
        {
            this.repositorio = repositorio;
            servicio = new ServicioCandidatosEventos(repositorio);
            this.registrar = registrar ?? (ex => ElmahHelper.Log(ex));
        }

        /// <summary>Punto de entrada de Hangfire (cada madrugada).</summary>
        public static Task CalcularTodos() => new CandidatosEventosJobsService().CalcularTodosAsync(DateTime.Today);

        /// <summary>Punto de entrada de Hangfire (al crear o anunciar un evento).</summary>
        public static Task CalcularEvento(int eventoId, string motivo) => new CandidatosEventosJobsService().CalcularEventoAsync(eventoId, DateTime.Today, motivo);

        public async Task<List<ResumenCandidatosEventoDTO>> CalcularTodosAsync(DateTime hoy)
        {
            var resumenes = new List<ResumenCandidatosEventoDTO>();
            List<int> eventos;
            try
            {
                if (!await repositorio.HayTabla().ConfigureAwait(false))
                {
                    return resumenes; // sin tabla, sin función
                }
                eventos = await repositorio.EventosFuturosActivos(hoy).ConfigureAwait(false) ?? new List<int>();
            }
            catch (Exception ex)
            {
                Registrar(new Exception($"[Eventos candidatos (sombra)] No se han podido leer los eventos: {ex.Message}", ex));
                return resumenes;
            }
            foreach (int evento in eventos)
            {
                ResumenCandidatosEventoDTO resumen = await CalcularEventoAsync(evento, hoy, ServicioCandidatosEventos.MOTIVO_DIARIO).ConfigureAwait(false);
                if (resumen != null)
                {
                    resumenes.Add(resumen);
                }
            }
            return resumenes;
        }

        public async Task<ResumenCandidatosEventoDTO> CalcularEventoAsync(int eventoId, DateTime hoy, string motivo)
        {
            try
            {
                return await servicio.Calcular(eventoId, hoy, motivo).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Registrar(new Exception($"[Eventos candidatos (sombra)] No se han podido calcular los candidatos del evento {eventoId}: {ex.Message}", ex));
                return null;
            }
        }

        private void Registrar(Exception ex)
        {
            try
            {
                registrar(ex);
            }
            catch
            {
                // Nunca tumba el job
            }
        }
    }
}
