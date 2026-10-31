using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.Entity;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Models;
using NestoAPI.Models.Agencias;

namespace NestoAPI.Infraestructure.Agencias.RepartoPropio
{
    /// <summary>
    /// NestoAPI#744: un hito público de la entrega: SOLO lo que se puede enseñar a quien tenga el enlace. A propósito no tiene
    /// DNI, firma, coordenadas, cobro ni el texto libre del repartidor: si no está en la clase, no puede salir en la página.
    /// </summary>
    public class HitoEntregaPublico
    {
        public string Tipo { get; set; }
        public DateTime FechaHora { get; set; }
        public string Receptor { get; set; }
        public string Motivo { get; set; }
    }

    // ===================================================================== NestoAPI#771: la trazabilidad del envío
    // Igual que el hito: solo fechas y números. Ninguna de estas clases tiene usuario, nombre de empleado, DNI, firma ni GPS.

    /// <summary>NestoAPI#771: otro envío (o este) del mismo pedido, de cualquier agencia.</summary>
    public class EnvioDelPedidoPublico
    {
        public int Numero { get; set; }
        public int Agencia { get; set; }
        public short Estado { get; set; }
        public DateTime Fecha { get; set; }
        public DateTime? FechaEntrega { get; set; }
    }

    /// <summary>NestoAPI#771: un picking del pedido y cuándo se empezó a recoger (primer escaneo PICK). Null si se hizo en papel.</summary>
    public class PickingDelPedidoPublico
    {
        public int Picking { get; set; }
        public DateTime? InicioPicking { get; set; }
    }

    /// <summary>NestoAPI#771: una caja del packing del pedido (EnviosAgenciaBultos): cuándo se cerró y si tiene foto.</summary>
    public class BultoTrazabilidad
    {
        public int Id { get; set; }
        public Guid IdCliente { get; set; }
        public int Picking { get; set; }
        /// <summary>La hora del móvil al cerrarla (FechaFoto) o, sin ella, la del servidor.</summary>
        public DateTime Fecha { get; set; }
        public bool TieneFoto { get; set; }
    }

    /// <summary>NestoAPI#771: una foto libre del packing del pedido (PreparacionFotosPacking).</summary>
    public class FotoPackingTrazabilidad
    {
        public int Id { get; set; }
        public Guid IdCliente { get; set; }
        public int Picking { get; set; }
        public DateTime Fecha { get; set; }
    }

    /// <summary>NestoAPI#771: todo lo que se sabe del recorrido del pedido de un envío, sin decidir aún qué es de este envío.</summary>
    public class DatosTrazabilidadEnvio
    {
        public DateTime? FechaPedido { get; set; }
        /// <summary>Todos los envíos del pedido (también este).</summary>
        public List<EnvioDelPedidoPublico> Envios { get; set; } = new List<EnvioDelPedidoPublico>();
        public List<PickingDelPedidoPublico> Pickings { get; set; } = new List<PickingDelPedidoPublico>();
        public List<BultoTrazabilidad> Bultos { get; set; } = new List<BultoTrazabilidad>();
        public List<FotoPackingTrazabilidad> FotosPacking { get; set; } = new List<FotoPackingTrazabilidad>();
        /// <summary>La primera lectura de un bulto de ESTE envío al cargar la furgoneta (EnviosAgenciaCargaBultos).</summary>
        public DateTime? PrimeraCarga { get; set; }
    }

    /// <summary>NestoAPI#771: una foto para la galería de la página (de una caja, como las de antes, o libre del packing).</summary>
    public class FotoDeLaGaleria
    {
        public int Id { get; set; }
        public Guid IdCliente { get; set; }
        public bool EsDelPacking { get; set; }
        public DateTime Fecha { get; set; }
    }

    /// <summary>NestoAPI#771: lo que se pinta de una foto de la galería (ya con su enlace firmado).</summary>
    public class FotoGaleriaPublica
    {
        public string Url { get; set; }
        public DateTime Fecha { get; set; }
    }

    /// <summary>NestoAPI#771: otro envío del mismo pedido, plegado debajo de la línea de tiempo.</summary>
    public class OtroEnvioPublico
    {
        public int Numero { get; set; }
        public string Estado { get; set; }
        public string Fecha { get; set; }
        /// <summary>Su propia página de seguimiento (solo los del reparto propio). Null si no hay.</summary>
        public string Url { get; set; }
    }

    /// <summary>Lo que lee la página pública (detrás de una interfaz para probarla sin BD).</summary>
    public interface ILectorSeguimientoPublicoReparto
    {
        /// <summary>El envío, sin seguimiento de EF. Null si no existe.</summary>
        Task<EnviosAgencia> LeerEnvio(int numero);
        /// <summary>Entregas e incidencias registradas por el repartidor, en orden. Vacía si no hay (o falta el script de #739).</summary>
        Task<List<HitoEntregaPublico>> LeerHitos(int envio);
        /// <summary>
        /// NestoAPI#771: la fecha del pedido, sus envíos, pickings, cajas y fotos del packing y la primera carga en la furgoneta de
        /// este envío. Lo que no se pueda leer (falta un script, la tabla no está…) va vacío: la página sale igual.
        /// </summary>
        Task<DatosTrazabilidadEnvio> LeerTrazabilidad(EnviosAgencia envio);
    }

    public class LectorSeguimientoPublicoReparto : ILectorSeguimientoPublicoReparto
    {
        private readonly NVEntities db;

        public LectorSeguimientoPublicoReparto(NVEntities db)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public async Task<EnviosAgencia> LeerEnvio(int numero)
            => (await db.EnviosAgencias.AsNoTracking().Where(e => e.Numero == numero).ToListAsync().ConfigureAwait(false)).FirstOrDefault();

        public async Task<List<HitoEntregaPublico>> LeerHitos(int envio)
        {
            if (!ColumnasOpcionales.ExisteTabla(db, RepositorioRepartoPropio.TABLA))
            {
                return new List<HitoEntregaPublico>();
            }
            // Solo estas cuatro columnas: nunca DniReceptor, Firma, Latitud/Longitud ni Texto.
            return await db.Database.SqlQuery<HitoEntregaPublico>(
                $"SELECT Tipo, FechaHora, Receptor, Motivo FROM dbo.{RepositorioRepartoPropio.TABLA} WHERE EnvioId = @p0 ORDER BY FechaHora, Id",
                envio).ToListAsync().ConfigureAwait(false);
        }

        // NestoAPI#771: ninguna consulta lee la columna Usuario (ni ninguna otra que diga quién lo hizo).
        internal const string SQL_BULTOS = @"
SELECT b.Id, b.IdCliente, b.Picking, ISNULL(b.FechaFoto, b.FechaRegistro) AS Fecha, CAST(CASE WHEN b.RutaBlob IS NULL THEN 0 ELSE 1 END AS bit) AS TieneFoto
FROM dbo.EnviosAgenciaBultos b WHERE b.Empresa = @p0 AND b.Pedido = @p1";

        internal const string SQL_FOTOS_PACKING = @"
SELECT f.Id, f.IdCliente, f.Picking, ISNULL(f.FechaFoto, f.FechaRegistro) AS Fecha
FROM dbo." + RepositorioPreparacionAlmacen.TABLA_FOTOS_PACKING + " f WHERE f.Empresa = @p0 AND f.Pedido = @p1";

        internal const string SQL_PICKINGS_DE_LAS_LINEAS = @"
SELECT DISTINCT l.Picking FROM LinPedidoVta l WHERE l.Empresa = @p0 AND l.[Número] = @p1 AND l.Picking > 0";

        // {0}: la lista de pickings (@p1, @p2…). Por IX_PreparacionEscaneos_Origen. En el picking por ola las lecturas no llevan
        // pedido: el primer escaneo del picking es cuando se empezó a recoger lo de todos sus pedidos.
        internal const string SQL_INICIO_PICKING = @"
SELECT e.NumeroOrigen AS Picking, MIN(e.FechaEscaneo) AS InicioPicking
FROM dbo.PreparacionEscaneos e
WHERE e.Empresa = @p0 AND e.TipoOrigen = 'PICK' AND e.Fase = 'PICK' AND e.NumeroOrigen IN ({0})
GROUP BY e.NumeroOrigen";

        internal const string SQL_PRIMERA_CARGA = "SELECT MIN(c.FechaHora) FROM dbo." + RepositorioCargaFurgoneta.TABLA + " c WHERE c.EnvioId = @p0";

        public async Task<DatosTrazabilidadEnvio> LeerTrazabilidad(EnviosAgencia envio)
        {
            var datos = new DatosTrazabilidadEnvio();
            if (envio == null)
            {
                return datos;
            }
            string empresa = envio.Empresa;
            int? pedido = envio.Pedido;

            await SinFallar("la carga", async () =>
            {
                if (ColumnasOpcionales.ExisteTabla(db, RepositorioCargaFurgoneta.TABLA))
                {
                    datos.PrimeraCarga = (await db.Database.SqlQuery<DateTime?>(SQL_PRIMERA_CARGA, envio.Numero).ToListAsync().ConfigureAwait(false)).FirstOrDefault();
                }
            }, envio.Numero).ConfigureAwait(false);

            if (!pedido.HasValue)
            {
                datos.Envios.Add(DeEnvio(envio));
                return datos;
            }

            await SinFallar("el pedido", async () =>
            {
                datos.FechaPedido = (await db.CabPedidoVtas.AsNoTracking().Where(c => c.Empresa == empresa && c.Número == pedido.Value)
                    .Select(c => c.Fecha).ToListAsync().ConfigureAwait(false)).FirstOrDefault();
            }, envio.Numero).ConfigureAwait(false);

            await SinFallar("los envíos del pedido", async () =>
            {
                datos.Envios = await db.EnviosAgencias.AsNoTracking().Where(e => e.Empresa == empresa && e.Pedido == pedido.Value)
                    .Select(e => new EnvioDelPedidoPublico { Numero = e.Numero, Agencia = e.Agencia, Estado = e.Estado, Fecha = e.Fecha, FechaEntrega = e.FechaEntrega })
                    .ToListAsync().ConfigureAwait(false);
            }, envio.Numero).ConfigureAwait(false);
            if (!datos.Envios.Any(e => e.Numero == envio.Numero))
            {
                datos.Envios.Add(DeEnvio(envio));
            }

            await SinFallar("los bultos", async () =>
            {
                if (ColumnasOpcionales.ExisteTabla(db, "EnviosAgenciaBultos"))
                {
                    datos.Bultos = await db.Database.SqlQuery<BultoTrazabilidad>(SQL_BULTOS, empresa, pedido.Value).ToListAsync().ConfigureAwait(false);
                }
            }, envio.Numero).ConfigureAwait(false);

            await SinFallar("las fotos del packing", async () =>
            {
                if (ColumnasOpcionales.ExisteTabla(db, RepositorioPreparacionAlmacen.TABLA_FOTOS_PACKING))
                {
                    datos.FotosPacking = await db.Database.SqlQuery<FotoPackingTrazabilidad>(SQL_FOTOS_PACKING, empresa, pedido.Value).ToListAsync().ConfigureAwait(false);
                }
            }, envio.Numero).ConfigureAwait(false);

            await SinFallar("los pickings", async () =>
            {
                List<int> deLasLineas = await db.Database.SqlQuery<int>(SQL_PICKINGS_DE_LAS_LINEAS, empresa, pedido.Value).ToListAsync().ConfigureAwait(false);
                List<int> todos = deLasLineas.Concat(datos.Bultos.Select(b => b.Picking)).Concat(datos.FotosPacking.Select(f => f.Picking))
                    .Where(p => p > 0).Distinct().OrderBy(p => p).ToList();
                var inicios = new Dictionary<int, DateTime?>();
                if (todos.Count > 0 && ColumnasOpcionales.ExisteTabla(db, "PreparacionEscaneos"))
                {
                    var parametros = new List<object> { empresa };
                    var nombres = new List<string>();
                    foreach (int picking in todos)
                    {
                        nombres.Add("@p" + parametros.Count);
                        parametros.Add(picking);
                    }
                    foreach (PickingDelPedidoPublico leido in await db.Database.SqlQuery<PickingDelPedidoPublico>(
                        string.Format(SQL_INICIO_PICKING, string.Join(", ", nombres)), parametros.ToArray()).ToListAsync().ConfigureAwait(false))
                    {
                        inicios[leido.Picking] = leido.InicioPicking;
                    }
                }
                datos.Pickings = todos.Select(p => new PickingDelPedidoPublico { Picking = p, InicioPicking = inicios.TryGetValue(p, out DateTime? inicio) ? inicio : null }).ToList();
            }, envio.Numero).ConfigureAwait(false);

            return datos;
        }

        private static EnvioDelPedidoPublico DeEnvio(EnviosAgencia envio)
            => new EnvioDelPedidoPublico { Numero = envio.Numero, Agencia = envio.Agencia, Estado = envio.Estado, Fecha = envio.Fecha, FechaEntrega = envio.FechaEntrega };

        private static async Task SinFallar(string que, Func<Task> leer, int envio)
        {
            try
            {
                await leer().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // La página sale igual, sin ese trozo de la historia
                ElmahHelper.Log(new Exception($"[Seguimiento #771] No se ha podido leer {que} del envío {envio}: {ex.Message}", ex));
            }
        }
    }

    /// <summary>
    /// NestoAPI#771: qué es de ESTE envío dentro de todo lo que se sabe del pedido, y los hitos de la línea de tiempo. Sin BD: se
    /// prueba entero.
    ///
    /// <para><b>Qué picking es de qué envío.</b> No hay ninguna columna que los una (EnviosAgenciaBultos.NumeroEnvio no se rellena
    /// y EnviosAgencia.Fecha no lleva hora). La fecha de un picking es la de su primer movimiento (escaneo PICK, caja o foto del
    /// packing). Los envíos del pedido se ordenan por número y cada picking es del primer día de envío igual o posterior a su
    /// fecha. Si ese día hay varios envíos del pedido: con tantos pickings como envíos, uno para cada uno en orden; si no, todos
    /// para todos (casi siempre es la misma salida etiquetada dos veces). Un picking posterior a todos los envíos es de un envío
    /// que aún no existe, y uno sin ninguna fecha no tiene nada que enseñar: ninguno de los dos se asigna.</para>
    /// </summary>
    public static class TrazabilidadEnvio
    {
        public const string PEDIDO_REALIZADO = "Pedido realizado";
        public const string RECOGIDA_SOLICITADA = "Recogida solicitada";
        public const string PREPARANDO = "Preparando su pedido";
        public const string EMBALADO = "Pedido embalado";
        public const string EN_EL_VEHICULO = "En el vehículo de reparto";
        public const string PREPARADO_PARA_EL_REPARTO = "Preparado para el reparto";
        /// <summary>Un máximo prudente de fotos en la página (una entrega normal lleva 1-3).</summary>
        public const int MAXIMO_FOTOS = 24;

        /// <summary>La fecha de cada picking: su primer movimiento conocido. Null si no hay ninguno.</summary>
        internal static Dictionary<int, DateTime?> FechasDeLosPickings(DatosTrazabilidadEnvio datos)
        {
            var fechas = new Dictionary<int, DateTime?>();
            void Apuntar(int picking, DateTime? fecha)
            {
                if (picking <= 0)
                {
                    return;
                }
                if (!fechas.TryGetValue(picking, out DateTime? actual) || actual == null || (fecha.HasValue && fecha.Value < actual.Value))
                {
                    fechas[picking] = fecha ?? actual;
                }
            }
            foreach (PickingDelPedidoPublico p in datos?.Pickings ?? new List<PickingDelPedidoPublico>())
            {
                Apuntar(p.Picking, p.InicioPicking);
            }
            foreach (BultoTrazabilidad b in datos?.Bultos ?? new List<BultoTrazabilidad>())
            {
                Apuntar(b.Picking, b.Fecha);
            }
            foreach (FotoPackingTrazabilidad f in datos?.FotosPacking ?? new List<FotoPackingTrazabilidad>())
            {
                Apuntar(f.Picking, f.Fecha);
            }
            return fechas;
        }

        /// <summary>Los pickings que son de <paramref name="envio"/> (ver la regla en la clase).</summary>
        public static HashSet<int> PickingsDelEnvio(int envio, DatosTrazabilidadEnvio datos)
        {
            var resultado = new HashSet<int>();
            List<EnvioDelPedidoPublico> envios = (datos?.Envios ?? new List<EnvioDelPedidoPublico>())
                .Where(e => e != null).GroupBy(e => e.Numero).Select(g => g.First()).OrderBy(e => e.Numero).ToList();
            if (!envios.Any(e => e.Numero == envio))
            {
                return resultado;
            }
            // Cada picking, al primer día de envío igual o posterior a su fecha
            var porDia = new Dictionary<DateTime, List<int>>();
            foreach (KeyValuePair<int, DateTime?> picking in FechasDeLosPickings(datos))
            {
                if (!picking.Value.HasValue)
                {
                    continue;
                }
                DateTime dia = picking.Value.Value.Date;
                List<EnvioDelPedidoPublico> posteriores = envios.Where(e => e.Fecha.Date >= dia).ToList();
                if (posteriores.Count == 0)
                {
                    continue; // de un envío que todavía no existe
                }
                DateTime diaEnvio = posteriores.Min(e => e.Fecha.Date);
                if (!porDia.TryGetValue(diaEnvio, out List<int> lista))
                {
                    porDia[diaEnvio] = lista = new List<int>();
                }
                lista.Add(picking.Key);
            }
            DateTime diaDeEste = envios.First(e => e.Numero == envio).Fecha.Date;
            if (!porDia.TryGetValue(diaDeEste, out List<int> pickingsDelDia))
            {
                return resultado;
            }
            List<EnvioDelPedidoPublico> delDia = envios.Where(e => e.Fecha.Date == diaDeEste).ToList();
            List<int> ordenados = pickingsDelDia.Distinct().OrderBy(p => p).ToList();
            if (delDia.Count > 1 && ordenados.Count == delDia.Count)
            {
                _ = resultado.Add(ordenados[delDia.FindIndex(e => e.Numero == envio)]);
                return resultado;
            }
            resultado.UnionWith(ordenados);
            return resultado;
        }

        /// <summary>
        /// Los hitos de antes del reparto, de este envío, por orden: el pedido, el picking, el embalaje y la furgoneta. Solo los
        /// que tienen dato. Si no se sabe ni el embalaje ni la carga y el envío ya salió, «Preparado para el reparto» (sin hora).
        /// </summary>
        public static List<EventoSeguimientoPublico> HitosPrevios(EnviosAgencia envio, DatosTrazabilidadEnvio datos, bool esRecogida)
        {
            var hitos = new List<EventoSeguimientoPublico>();
            if (datos?.FechaPedido != null)
            {
                hitos.Add(Evento(datos.FechaPedido.Value, esRecogida ? RECOGIDA_SOLICITADA : PEDIDO_REALIZADO, 0));
            }
            HashSet<int> pickings = PickingsDelEnvio(envio.Numero, datos);
            DateTime? inicio = (datos?.Pickings ?? new List<PickingDelPedidoPublico>())
                .Where(p => pickings.Contains(p.Picking) && p.InicioPicking.HasValue).Select(p => p.InicioPicking).Min();
            if (inicio.HasValue)
            {
                hitos.Add(Evento(inicio.Value, PREPARANDO, 1));
            }
            DateTime? embalado = (datos?.Bultos ?? new List<BultoTrazabilidad>()).Where(b => pickings.Contains(b.Picking)).Select(b => (DateTime?)b.Fecha)
                .Concat((datos?.FotosPacking ?? new List<FotoPackingTrazabilidad>()).Where(f => pickings.Contains(f.Picking)).Select(f => (DateTime?)f.Fecha))
                .Max();
            if (embalado.HasValue)
            {
                hitos.Add(Evento(embalado.Value, EMBALADO, 2));
            }
            if (datos?.PrimeraCarga != null)
            {
                hitos.Add(Evento(datos.PrimeraCarga.Value, EN_EL_VEHICULO, 3));
            }
            else if (!embalado.HasValue && envio.Estado >= Constantes.Agencias.ESTADO_TRAMITADO)
            {
                hitos.Add(new EventoSeguimientoPublico { Fecha = envio.Fecha, ConHora = false, Titulo = esRecogida ? "Recogida programada" : PREPARADO_PARA_EL_REPARTO, Orden = 3 });
            }
            return hitos;
        }

        /// <summary>Las fotos de las cajas y las libres del packing de este envío, de la más antigua a la más reciente.</summary>
        public static List<FotoDeLaGaleria> Galeria(EnviosAgencia envio, DatosTrazabilidadEnvio datos)
        {
            HashSet<int> pickings = PickingsDelEnvio(envio.Numero, datos);
            return (datos?.Bultos ?? new List<BultoTrazabilidad>())
                .Where(b => b.TieneFoto && pickings.Contains(b.Picking))
                .Select(b => new FotoDeLaGaleria { Id = b.Id, IdCliente = b.IdCliente, EsDelPacking = false, Fecha = b.Fecha })
                .Concat((datos?.FotosPacking ?? new List<FotoPackingTrazabilidad>())
                    .Where(f => pickings.Contains(f.Picking))
                    .Select(f => new FotoDeLaGaleria { Id = f.Id, IdCliente = f.IdCliente, EsDelPacking = true, Fecha = f.Fecha }))
                .OrderBy(f => f.Fecha).ThenBy(f => f.EsDelPacking).ThenBy(f => f.Id)
                .Take(MAXIMO_FOTOS)
                .ToList();
        }

        /// <summary>«En reparto», «Entregado»…: el estado de otro envío del pedido, de cualquier agencia, para el cliente.</summary>
        public static string EstadoLegible(EnvioDelPedidoPublico envio)
        {
            switch (envio.Estado)
            {
                case Constantes.Agencias.ESTADO_TRAMITADO:
                    return envio.Agencia == Constantes.Agencias.AGENCIA_REPARTO_PROPIO ? "En reparto" : "En camino";
                case Constantes.Agencias.ESTADO_ENTREGADO: return "Entregado";
                case Constantes.Agencias.ESTADO_INCIDENTADO: return "Incidencia en la entrega";
                case Constantes.Agencias.ESTADO_DEVUELTO: return "Devuelto";
                case Constantes.Agencias.ESTADO_CERRADO_A_MANO: return "Cerrado";
                default: return "En preparación";
            }
        }

        /// <summary>Los demás envíos del pedido, del más antiguo al más reciente, con su enlace si son del reparto propio.</summary>
        public static List<OtroEnvioPublico> OtrosEnvios(EnviosAgencia envio, DatosTrazabilidadEnvio datos, Func<int, string> urlSeguimiento)
        {
            return (datos?.Envios ?? new List<EnvioDelPedidoPublico>())
                .Where(e => e != null && e.Numero != envio.Numero)
                .GroupBy(e => e.Numero).Select(g => g.First())
                .OrderBy(e => e.Numero)
                .Select(e => new OtroEnvioPublico
                {
                    Numero = e.Numero,
                    Estado = EstadoLegible(e),
                    Fecha = e.Estado == Constantes.Agencias.ESTADO_ENTREGADO && e.FechaEntrega.HasValue
                        ? "Entregado el " + PaginaHtmlSeguimientoReparto.FechaCorta(e.FechaEntrega.Value)
                        : "Envío del " + PaginaHtmlSeguimientoReparto.FechaCorta(e.Fecha),
                    Url = e.Agencia == Constantes.Agencias.AGENCIA_REPARTO_PROPIO ? urlSeguimiento?.Invoke(e.Numero) : null
                })
                .ToList();
        }

        private static EventoSeguimientoPublico Evento(DateTime fecha, string titulo, int orden)
            => new EventoSeguimientoPublico { Fecha = fecha, ConHora = fecha.TimeOfDay != TimeSpan.Zero, Titulo = titulo, Orden = orden };
    }

    /// <summary>NestoAPI#744: la respuesta de la página: el código HTTP y el HTML.</summary>
    public class PaginaSeguimientoReparto
    {
        public HttpStatusCode Estado { get; set; }
        public string Html { get; set; }
    }

    /// <summary>
    /// NestoAPI#744: la página pública de seguimiento de los envíos del reparto propio (<c>GET /seguimiento/{token}</c>, sin
    /// usuario). El token lo firma <see cref="EnlaceSeguimientoRepartoPropio"/>; lo único que protege la página es esa firma.
    /// Enseña el pedido, los bultos, el estado y su historia (las entregas e incidencias que registra el repartidor desde Ariadna,
    /// las mismas que dejan su fila en EnviosHistoria pero con el motivo estructurado y sin el cobro ni el texto libre) y, si está
    /// entregado, cuándo y quién lo recogió. Nunca el DNI, la firma ni la ubicación.
    /// NestoAPI#771: y la trazabilidad completa de este envío (pedido, picking, embalaje, furgoneta), la galería de fotos del
    /// packing y los otros envíos del mismo pedido, plegados.
    /// </summary>
    public class ServicioSeguimientoPublicoReparto
    {
        private readonly ILectorSeguimientoPublicoReparto lector;
        private readonly Func<string, (bool Valido, int Envio)> leerToken;
        private readonly Func<EnviosAgencia, Task<bool>> esRecogida;
        private readonly Func<string> claveFotos;

        /// <param name="esRecogida">NestoAPI#759: ¿el envío es la parada de una nota de RECOGIDA? Sin él, nunca.</param>
        /// <param name="claveFotos">NestoAPI#771: la clave de los enlaces públicos de las fotos. Sin ella, la del Web.config.</param>
        public ServicioSeguimientoPublicoReparto(ILectorSeguimientoPublicoReparto lector, Func<string, (bool, int)> leerToken = null,
            Func<EnviosAgencia, Task<bool>> esRecogida = null, Func<string> claveFotos = null)
        {
            this.lector = lector ?? throw new ArgumentNullException(nameof(lector));
            this.leerToken = leerToken ?? (t => (EnlaceSeguimientoRepartoPropio.TryLeerEnvio(t, out int envio), envio));
            this.esRecogida = esRecogida;
            this.claveFotos = claveFotos ?? (() => ConfigurationManager.AppSettings[EnlacePublicoFotoBulto.CLAVE_CONFIGURACION]);
        }

        /// <summary>NestoAPI#759: con la BD, mira si el pedido del envío es una nota de recogida.</summary>
        public static Func<EnviosAgencia, Task<bool>> EsRecogidaEnBaseDeDatos(NVEntities db)
            => async envio =>
            {
                if (envio?.Pedido == null)
                {
                    return false;
                }
                List<NotaParadaReparto> notas = await new RepositorioNotasParadaReparto(db).LeerNotas(new[] { envio.Pedido.Value }).ConfigureAwait(false);
                return notas.Any(n => n.EsRecogida);
            };

        public async Task<PaginaSeguimientoReparto> Pagina(string token)
        {
            (bool valido, int numero) = leerToken(token);
            if (!valido)
            {
                return NoEncontrada();
            }
            EnviosAgencia envio = await lector.LeerEnvio(numero).ConfigureAwait(false);
            // El mismo 404 para un token malo, un envío que no existe o uno que no es del reparto propio
            if (envio == null || envio.Agencia != Constantes.Agencias.AGENCIA_REPARTO_PROPIO)
            {
                return NoEncontrada();
            }
            List<HitoEntregaPublico> hitos = await lector.LeerHitos(envio.Numero).ConfigureAwait(false) ?? new List<HitoEntregaPublico>();
            bool recogida = false;
            if (esRecogida != null)
            {
                try
                {
                    recogida = await esRecogida(envio).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // La página sale igual, con los textos de una entrega.
                    ElmahHelper.Log(new Exception($"[Seguimiento #759] No se pudo saber si el envío {envio.Numero} es una recogida: {ex.Message}", ex));
                }
            }
            DatosTrazabilidadEnvio datos = null;
            try
            {
                datos = await lector.LeerTrazabilidad(envio).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Sin la trazabilidad, la página de siempre
                ElmahHelper.Log(new Exception($"[Seguimiento #771] No se pudo leer la trazabilidad del envío {envio.Numero}: {ex.Message}", ex));
            }
            string clave = claveFotos();
            string urlBase = (EnlaceSeguimientoRepartoPropio.LeerUrlBase() ?? string.Empty).Trim().TrimEnd('/');
            VistaSeguimientoReparto vista = VistaSeguimientoReparto.Construir(envio, hitos, recogida, datos,
                urlFoto: f => UrlFoto(urlBase, clave, f),
                urlSeguimiento: EnlaceSeguimientoRepartoPropio.Url);
            return new PaginaSeguimientoReparto { Estado = HttpStatusCode.OK, Html = PaginaHtmlSeguimientoReparto.Html(vista) };
        }

        /// <summary>NestoAPI#771: el enlace público y firmado de la foto (el de siempre: api/Almacen/Fotos/{token}). Null sin clave.</summary>
        internal static string UrlFoto(string urlBase, string clave, FotoDeLaGaleria foto)
        {
            string ruta = foto.EsDelPacking
                ? EnlacePublicoFotoBulto.RutaFotoPacking(clave, foto.Id, foto.IdCliente)
                : EnlacePublicoFotoBulto.Ruta(clave, foto.Id, foto.IdCliente);
            return ruta == null ? null : urlBase + "/" + ruta;
        }

        public static PaginaSeguimientoReparto NoEncontrada()
            => new PaginaSeguimientoReparto { Estado = HttpStatusCode.NotFound, Html = PaginaHtmlSeguimientoReparto.HtmlNoEncontrado() };
    }

    public enum EstadoSeguimientoPublico
    {
        Pendiente,
        EnReparto,
        Incidencia,
        Entregado,
        Devuelto,
        Cerrado
    }

    public class EventoSeguimientoPublico
    {
        public DateTime Fecha { get; set; }
        public bool ConHora { get; set; }
        public string Titulo { get; set; }
        public string Detalle { get; set; }
        /// <summary>
        /// NestoAPI#771: la fase en la línea de tiempo (0 pedido, 1 picking, 2 embalaje, 3 furgoneta, 4 reparto). Se ordena por
        /// fase y, dentro, por fecha: si el reloj de una PDA va mal, el embalaje no sale antes que el pedido.
        /// </summary>
        public int Orden { get; set; } = 4;
    }

    /// <summary>Lo que se pinta, ya decidido (sin HTML): fácil de probar.</summary>
    public class VistaSeguimientoReparto
    {
        public int? Pedido { get; set; }
        public short Bultos { get; set; }
        public EstadoSeguimientoPublico Estado { get; set; }
        public string TituloEstado { get; set; }
        public string TextoEstado { get; set; }
        public DateTime? FechaEntrega { get; set; }
        public string Receptor { get; set; }
        public string MotivoIncidencia { get; set; }
        /// <summary>NestoAPI#759: la parada de una nota de recogida («Recogida programada» / «Recogido»).</summary>
        public bool EsRecogida { get; set; }
        /// <summary>NestoAPI#771: la línea de tiempo, del pedido a la entrega (lo más antiguo primero).</summary>
        public List<EventoSeguimientoPublico> Eventos { get; set; } = new List<EventoSeguimientoPublico>();
        /// <summary>NestoAPI#771: las fotos del packing de este envío. Vacía: no sale la galería.</summary>
        public List<FotoGaleriaPublica> Fotos { get; set; } = new List<FotoGaleriaPublica>();
        /// <summary>NestoAPI#771: los demás envíos del pedido. Vacía: no sale la sección.</summary>
        public List<OtroEnvioPublico> OtrosEnvios { get; set; } = new List<OtroEnvioPublico>();
        public bool MostrarContacto => Estado == EstadoSeguimientoPublico.Incidencia || Estado == EstadoSeguimientoPublico.Devuelto
            || Estado == EstadoSeguimientoPublico.Cerrado;

        /// <summary>Para el cliente, de usted: qué le pasó a su envío (no el código de Ariadna).</summary>
        internal static string MotivoLegible(string motivo)
        {
            switch (motivo?.Trim())
            {
                case IncidenciaRepartoDTO.AUSENTE: return "No había nadie en la dirección de entrega.";
                case IncidenciaRepartoDTO.DIRECCION_INCORRECTA: return "La dirección de entrega no es correcta o está incompleta.";
                case IncidenciaRepartoDTO.RECHAZADO: return "El envío no se aceptó en la entrega.";
                case IncidenciaRepartoDTO.FALTAN_BULTOS: return "Faltan bultos del envío: lo estamos revisando.";
                case IncidenciaRepartoDTO.NADA_QUE_RECOGER: return "No había nada que recoger en la dirección.";
                default: return "Ha surgido un problema con la entrega.";
            }
        }

        public static VistaSeguimientoReparto Construir(EnviosAgencia envio, IEnumerable<HitoEntregaPublico> hitos, bool esRecogida = false,
            DatosTrazabilidadEnvio datos = null, Func<FotoDeLaGaleria, string> urlFoto = null, Func<int, string> urlSeguimiento = null)
        {
            List<HitoEntregaPublico> lista = (hitos ?? Enumerable.Empty<HitoEntregaPublico>()).Where(h => h != null).OrderBy(h => h.FechaHora).ToList();
            HitoEntregaPublico entrega = lista.LastOrDefault(h => string.Equals(h.Tipo?.Trim(), PruebaEntregaReparto.TIPO_ENTREGA, StringComparison.OrdinalIgnoreCase));
            HitoEntregaPublico incidencia = lista.LastOrDefault(h => string.Equals(h.Tipo?.Trim(), PruebaEntregaReparto.TIPO_INCIDENCIA, StringComparison.OrdinalIgnoreCase));

            var vista = new VistaSeguimientoReparto { Pedido = envio.Pedido, Bultos = envio.Bultos, EsRecogida = esRecogida };
            string hecho = esRecogida ? "Recogido" : "Entregado";

            switch (envio.Estado)
            {
                case Constantes.Agencias.ESTADO_ENTREGADO:
                    vista.Estado = EstadoSeguimientoPublico.Entregado;
                    vista.TituloEstado = hecho;
                    vista.FechaEntrega = entrega?.FechaHora ?? envio.FechaEntrega;
                    vista.Receptor = Limpio(entrega?.Receptor);
                    vista.TextoEstado = esRecogida
                        ? "Ya hemos pasado a recogerlo. Gracias por confiar en Nueva Visión."
                        : "Su pedido ya está entregado. Gracias por confiar en Nueva Visión.";
                    break;
                case Constantes.Agencias.ESTADO_INCIDENTADO:
                    vista.Estado = EstadoSeguimientoPublico.Incidencia;
                    vista.TituloEstado = esRecogida ? "Incidencia en la recogida" : "Incidencia en la entrega";
                    vista.MotivoIncidencia = MotivoLegible(incidencia?.Motivo);
                    vista.TextoEstado = vista.MotivoIncidencia + " Volveremos a intentarlo o nos pondremos en contacto con usted.";
                    break;
                case Constantes.Agencias.ESTADO_TRAMITADO:
                    vista.Estado = EstadoSeguimientoPublico.EnReparto;
                    vista.TituloEstado = esRecogida ? "Recogida programada" : "En reparto";
                    vista.TextoEstado = esRecogida
                        ? "Tenemos programada la recogida: pasaremos a recogerlo en breve."
                        : "Su pedido está en nuestro reparto y se lo llevaremos en breve.";
                    break;
                case Constantes.Agencias.ESTADO_DEVUELTO:
                    vista.Estado = EstadoSeguimientoPublico.Devuelto;
                    vista.TituloEstado = "Devuelto a nuestro almacén";
                    vista.TextoEstado = "No hemos podido entregar el pedido y ha vuelto a nuestro almacén. Póngase en contacto con nosotros, por favor.";
                    break;
                case Constantes.Agencias.ESTADO_CERRADO_A_MANO:
                    vista.Estado = EstadoSeguimientoPublico.Cerrado;
                    vista.TituloEstado = "Envío cerrado";
                    vista.TextoEstado = "Este envío está cerrado. Si tiene cualquier duda, póngase en contacto con nosotros.";
                    break;
                default:
                    vista.Estado = EstadoSeguimientoPublico.Pendiente;
                    vista.TituloEstado = "Preparando su pedido";
                    vista.TextoEstado = "Estamos preparando su pedido. En cuanto salga a reparto lo verá aquí.";
                    break;
            }

            // NestoAPI#771: lo de antes del reparto (pedido, picking, embalaje, furgoneta); sin datos, «Preparado para el reparto»
            vista.Eventos.AddRange(TrazabilidadEnvio.HitosPrevios(envio, datos, esRecogida));
            // Lo que ha registrado el repartidor
            foreach (HitoEntregaPublico hito in lista)
            {
                if (string.Equals(hito.Tipo?.Trim(), PruebaEntregaReparto.TIPO_ENTREGA, StringComparison.OrdinalIgnoreCase))
                {
                    string receptor = Limpio(hito.Receptor);
                    vista.Eventos.Add(new EventoSeguimientoPublico
                    {
                        Fecha = hito.FechaHora,
                        ConHora = true,
                        Titulo = hecho,
                        Detalle = receptor == null ? null : (esRecogida ? "Lo entregó " : "Lo recogió ") + receptor + "."
                    });
                }
                else if (string.Equals(hito.Tipo?.Trim(), PruebaEntregaReparto.TIPO_INCIDENCIA, StringComparison.OrdinalIgnoreCase))
                {
                    vista.Eventos.Add(new EventoSeguimientoPublico { Fecha = hito.FechaHora, ConHora = true, Titulo = "Incidencia", Detalle = MotivoLegible(hito.Motivo) });
                }
            }
            // Entregado sin prueba del repartidor (p. ej. cerrado desde Nesto): la fecha del envío.
            if (vista.Estado == EstadoSeguimientoPublico.Entregado && entrega == null && envio.FechaEntrega.HasValue)
            {
                vista.Eventos.Add(new EventoSeguimientoPublico { Fecha = envio.FechaEntrega.Value, ConHora = envio.FechaEntrega.Value.TimeOfDay != TimeSpan.Zero, Titulo = hecho });
            }
            // NestoAPI#771: una línea de tiempo, del pedido a la entrega (por fase y, dentro, por fecha)
            vista.Eventos = vista.Eventos.OrderBy(e => e.Orden).ThenBy(e => e.Fecha).ToList();

            if (datos != null && urlFoto != null)
            {
                vista.Fotos = TrazabilidadEnvio.Galeria(envio, datos)
                    .Select(f => new FotoGaleriaPublica { Url = urlFoto(f), Fecha = f.Fecha })
                    .Where(f => !string.IsNullOrWhiteSpace(f.Url))
                    .ToList();
            }
            if (datos != null)
            {
                vista.OtrosEnvios = TrazabilidadEnvio.OtrosEnvios(envio, datos, urlSeguimiento);
            }
            return vista;
        }

        private static string Limpio(string texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();
    }

    /// <summary>NestoAPI#744: el HTML de la página (marca Nueva Visión, móvil primero, claro/oscuro, de usted).</summary>
    public static class PaginaHtmlSeguimientoReparto
    {
        public const string TELEFONO = "916 28 19 14";
        public const string TELEFONO_ENLACE = "+34916281914";
        public const string WHATSAPP = "https://wa.me/34916281914";
        public const string CORREO = Constantes.Correos.LOGISTICA;
        internal const string URL_LOGO = "https://www.productosdeesteticaypeluqueriaprofesional.com/img/cms/Landing/logo.png";
        private static readonly CultureInfo Espanol = new CultureInfo("es-ES");

        public static string Html(VistaSeguimientoReparto vista)
        {
            var cuerpo = new StringBuilder();
            _ = cuerpo.Append("<p class=\"etiqueta\">Seguimiento de su envío</p>");
            _ = cuerpo.Append("<div class=\"estado estado-").Append(Clase(vista.Estado)).Append("\">")
                .Append("<h1>").Append(H(vista.TituloEstado)).Append("</h1>")
                .Append("<p>").Append(H(vista.TextoEstado)).Append("</p>");
            if (vista.Estado == EstadoSeguimientoPublico.Entregado && vista.FechaEntrega.HasValue)
            {
                _ = cuerpo.Append("<p class=\"dato\"><strong>").Append(H(FechaLarga(vista.FechaEntrega.Value, vista.FechaEntrega.Value.TimeOfDay != TimeSpan.Zero))).Append("</strong></p>");
            }
            if (vista.Estado == EstadoSeguimientoPublico.Entregado && vista.Receptor != null)
            {
                _ = cuerpo.Append("<p class=\"dato\">").Append(vista.EsRecogida ? "Lo entregó" : "Lo recogió").Append(" <strong>").Append(H(vista.Receptor)).Append("</strong></p>");
            }
            _ = cuerpo.Append("</div>");

            _ = cuerpo.Append("<dl class=\"resumen\">");
            if (vista.Pedido.HasValue)
            {
                _ = cuerpo.Append("<div><dt>Pedido</dt><dd>").Append(vista.Pedido.Value.ToString(CultureInfo.InvariantCulture)).Append("</dd></div>");
            }
            _ = cuerpo.Append("<div><dt>Bultos</dt><dd>").Append(vista.Bultos.ToString(CultureInfo.InvariantCulture)).Append("</dd></div>");
            _ = cuerpo.Append("</dl>");

            if (vista.Eventos.Count > 0)
            {
                // NestoAPI#771: la línea de tiempo, del pedido a lo último (lo último, destacado)
                _ = cuerpo.Append("<h2>Historia del envío</h2><ol class=\"historia\">");
                for (int i = 0; i < vista.Eventos.Count; i++)
                {
                    EventoSeguimientoPublico evento = vista.Eventos[i];
                    _ = cuerpo.Append(i == vista.Eventos.Count - 1 ? "<li class=\"ultimo\">" : "<li>")
                        .Append("<span class=\"que\">").Append(H(evento.Titulo)).Append("</span>")
                        .Append("<span class=\"cuando\">").Append(H(FechaLarga(evento.Fecha, evento.ConHora))).Append("</span>");
                    if (!string.IsNullOrEmpty(evento.Detalle))
                    {
                        _ = cuerpo.Append("<span class=\"detalle\">").Append(H(evento.Detalle)).Append("</span>");
                    }
                    _ = cuerpo.Append("</li>");
                }
                _ = cuerpo.Append("</ol>");
            }

            _ = cuerpo.Append(Galeria(vista.Fotos));
            _ = cuerpo.Append(OtrosEnvios(vista.OtrosEnvios));
            _ = cuerpo.Append(Contacto(vista.MostrarContacto));
            return Documento("Seguimiento de su envío", cuerpo.ToString());
        }

        /// <summary>
        /// NestoAPI#771: las miniaturas; al tocar una se amplía a pantalla completa (CSS :target, sin JavaScript) y al tocar otra
        /// vez se cierra. La imagen grande no se descarga hasta que se abre (loading="lazy" en un elemento oculto).
        /// </summary>
        internal static string Galeria(IList<FotoGaleriaPublica> fotos)
        {
            if (fotos == null || fotos.Count == 0)
            {
                return string.Empty;
            }
            var html = new StringBuilder();
            _ = html.Append("<h2 id=\"fotos\">Fotos de su pedido</h2>")
                .Append("<p class=\"nota\">Así lo dejamos preparado en nuestro almacén. Toque una foto para verla en grande.</p>")
                .Append("<div class=\"galeria\">");
            for (int i = 0; i < fotos.Count; i++)
            {
                string alt = "Foto " + (i + 1).ToString(CultureInfo.InvariantCulture) + " del embalaje";
                _ = html.Append("<a class=\"miniatura\" href=\"#foto-").Append(i + 1).Append("\">")
                    .Append("<img src=\"").Append(H(fotos[i].Url)).Append("\" alt=\"").Append(alt).Append("\" loading=\"lazy\"></a>");
            }
            _ = html.Append("</div>");
            for (int i = 0; i < fotos.Count; i++)
            {
                string alt = "Foto " + (i + 1).ToString(CultureInfo.InvariantCulture) + " del embalaje, en grande";
                _ = html.Append("<a class=\"ampliada\" id=\"foto-").Append(i + 1).Append("\" href=\"#fotos\" aria-label=\"Cerrar la foto\">")
                    .Append("<img src=\"").Append(H(fotos[i].Url)).Append("\" alt=\"").Append(alt).Append("\" loading=\"lazy\">")
                    .Append("<span class=\"cerrar\">Cerrar ✕</span></a>");
            }
            return html.ToString();
        }

        /// <summary>NestoAPI#771: los demás envíos del pedido, plegados (details/summary), con su enlace si son del reparto propio.</summary>
        internal static string OtrosEnvios(IList<OtroEnvioPublico> otros)
        {
            if (otros == null || otros.Count == 0)
            {
                return string.Empty;
            }
            var html = new StringBuilder();
            _ = html.Append("<details class=\"otros\"><summary>Otros envíos de este pedido (").Append(otros.Count).Append(")</summary><ul>");
            foreach (OtroEnvioPublico otro in otros)
            {
                _ = html.Append("<li><span class=\"que\">").Append(H(otro.Estado)).Append("</span>")
                    .Append("<span class=\"cuando\">").Append(H(otro.Fecha)).Append("</span>");
                if (!string.IsNullOrWhiteSpace(otro.Url))
                {
                    _ = html.Append("<a href=\"").Append(H(otro.Url)).Append("\">Ver el seguimiento de este envío</a>");
                }
                _ = html.Append("</li>");
            }
            _ = html.Append("</ul></details>");
            return html.ToString();
        }

        public static string HtmlNoEncontrado()
            => Documento("Envío no encontrado",
                "<div class=\"estado estado-pendiente\"><h1>No encontramos este envío</h1>" +
                "<p>El enlace no es correcto o ya no está disponible. Compruebe que lo ha copiado entero o póngase en contacto con nosotros " +
                "y le diremos cómo va su pedido.</p></div>" + Contacto(true));

        private static string Contacto(bool destacado)
            => "<div class=\"contacto" + (destacado ? " destacado" : string.Empty) + "\">" +
               "<p>" + (destacado ? "¿Necesita ayuda? Estamos a su disposición:" : "¿Alguna duda? Estamos a su disposición:") + "</p>" +
               "<p><a href=\"tel:" + TELEFONO_ENLACE + "\">" + TELEFONO + "</a> · <a href=\"" + WHATSAPP + "\">WhatsApp</a> · " +
               "<a href=\"mailto:" + CORREO + "\">" + CORREO + "</a></p></div>";

        // internal: también la usa la página de los enlaces de descarga (NestoAPI#737)
        internal static string Documento(string titulo, string cuerpo)
            => "<!DOCTYPE html><html lang=\"es\"><head><meta charset=\"utf-8\">" +
               "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">" +
               "<meta name=\"robots\" content=\"noindex, nofollow\">" +
               "<meta name=\"referrer\" content=\"no-referrer\">" +
               "<meta name=\"color-scheme\" content=\"light dark\">" +
               "<title>" + H(titulo) + " · Nueva Visión</title><style>" + Estilos + "</style></head><body><main>" +
               "<header><span class=\"logo\"><img src=\"" + URL_LOGO + "\" alt=\"Nueva Visión\"></span></header>" +
               cuerpo +
               "<footer>Nueva Visión</footer></main></body></html>";

        private const string Estilos =
            ":root{--fondo:#f5f2f4;--tarjeta:#fff;--texto:#2b2630;--suave:#6f6578;--marca:#7b6e8e;--borde:#e4dde8;" +
            "--ok:#1e7d4f;--okf:#e5f4ec;--aviso:#a14b00;--avisof:#fdf0e3;--info:#5b4f7a;--infof:#efebf6;}" +
            "@media (prefers-color-scheme:dark){:root{--fondo:#16141a;--tarjeta:#221f28;--texto:#ece8f0;--suave:#aba3b4;--marca:#b9acd0;" +
            "--borde:#38323f;--ok:#7fd3a6;--okf:#183226;--aviso:#f3b27a;--avisof:#3a2717;--info:#c9bde3;--infof:#2c2738;}}" +
            "*{box-sizing:border-box}body{margin:0;background:var(--fondo);color:var(--texto);" +
            "font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,sans-serif;font-size:16px;line-height:1.5}" +
            "main{max-width:520px;margin:0 auto;padding:16px}" +
            "header{text-align:center;padding:12px 0 16px}.logo{display:inline-block;background:#fff;border-radius:12px;padding:8px 16px}" +
            ".logo img{max-height:56px;max-width:100%;display:block}" +
            ".etiqueta{color:var(--suave);font-size:14px;margin:0 0 8px;text-transform:uppercase;letter-spacing:.05em}" +
            ".estado{border-radius:14px;padding:16px;margin-bottom:16px;background:var(--infof);border:1px solid var(--borde)}" +
            ".estado h1{font-size:22px;margin:0 0 6px;color:var(--info)}.estado p{margin:4px 0}" +
            ".estado-entregado{background:var(--okf)}.estado-entregado h1{color:var(--ok)}" +
            ".estado-incidencia,.estado-devuelto{background:var(--avisof)}.estado-incidencia h1,.estado-devuelto h1{color:var(--aviso)}" +
            ".resumen{display:flex;gap:12px;margin:0 0 16px}.resumen div{flex:1;background:var(--tarjeta);border:1px solid var(--borde);" +
            "border-radius:12px;padding:12px}.resumen dt{color:var(--suave);font-size:13px}.resumen dd{margin:0;font-size:20px;font-weight:600}" +
            "h2{font-size:16px;color:var(--marca);margin:20px 0 8px}" +
            // NestoAPI#771: la línea de tiempo (un punto por hito unido por una línea; el último, relleno)
            ".historia{list-style:none;margin:0;padding:12px 16px;background:var(--tarjeta);border:1px solid var(--borde);border-radius:12px}" +
            ".historia li{position:relative;padding:0 0 14px 24px;display:flex;flex-direction:column}" +
            ".historia li:last-child{padding-bottom:0}" +
            ".historia li:before{content:'';position:absolute;left:4px;top:7px;width:10px;height:10px;border-radius:50%;" +
            "border:2px solid var(--marca);background:var(--tarjeta)}" +
            ".historia li:after{content:'';position:absolute;left:9px;top:21px;bottom:-5px;width:2px;background:var(--borde)}" +
            ".historia li:last-child:after{display:none}.historia li.ultimo:before{background:var(--marca)}" +
            ".cuando{color:var(--suave);font-size:13px}.que{font-weight:600}.detalle{font-size:14px}" +
            ".nota{color:var(--suave);font-size:14px;margin:0 0 8px}" +
            ".galeria{display:grid;grid-template-columns:repeat(auto-fill,minmax(96px,1fr));gap:8px}" +
            ".miniatura{display:block;aspect-ratio:1/1;border-radius:10px;overflow:hidden;border:1px solid var(--borde);background:var(--tarjeta)}" +
            ".miniatura img{width:100%;height:100%;object-fit:cover;display:block}" +
            ".ampliada{display:none;position:fixed;inset:0;z-index:10;background:rgba(0,0,0,.92);align-items:center;justify-content:center;" +
            "flex-direction:column;padding:16px;text-decoration:none}.ampliada:target{display:flex}" +
            ".ampliada img{max-width:100%;max-height:85vh;object-fit:contain;border-radius:8px}" +
            ".cerrar{color:#fff;margin-top:12px;font-weight:600}" +
            ".otros{margin-top:20px;background:var(--tarjeta);border:1px solid var(--borde);border-radius:12px;padding:12px 16px}" +
            ".otros summary{cursor:pointer;font-weight:600;color:var(--marca)}" +
            ".otros ul{list-style:none;margin:8px 0 0;padding:0}.otros li{padding:8px 0;border-top:1px solid var(--borde);display:flex;flex-direction:column}" +
            ".contacto{margin-top:20px;padding:12px 16px;border-radius:12px;font-size:14px;color:var(--suave)}" +
            ".contacto.destacado{background:var(--tarjeta);border:1px solid var(--borde);color:var(--texto)}.contacto p{margin:4px 0}" +
            "a{color:var(--marca);font-weight:600;word-break:break-word}" +
            "footer{text-align:center;color:var(--suave);font-size:12px;padding:24px 0 8px}";

        private static string Clase(EstadoSeguimientoPublico estado)
        {
            switch (estado)
            {
                case EstadoSeguimientoPublico.Entregado: return "entregado";
                case EstadoSeguimientoPublico.Incidencia: return "incidencia";
                case EstadoSeguimientoPublico.Devuelto: return "devuelto";
                case EstadoSeguimientoPublico.EnReparto: return "reparto";
                case EstadoSeguimientoPublico.Cerrado: return "cerrado";
                default: return "pendiente";
            }
        }

        /// <summary>«jueves, 29 de octubre de 2026, 12:45» (sin hora si no la tiene).</summary>
        internal static string FechaLarga(DateTime fecha, bool conHora)
            => conHora
                ? fecha.ToString("dddd, d 'de' MMMM 'de' yyyy, HH:mm", Espanol)
                : fecha.ToString("dddd, d 'de' MMMM 'de' yyyy", Espanol);

        /// <summary>NestoAPI#771: «29 de octubre de 2026».</summary>
        internal static string FechaCorta(DateTime fecha) => fecha.ToString("d 'de' MMMM 'de' yyyy", Espanol);

        // Solo los caracteres que rompen el HTML: WebUtility.HtmlEncode de .NET Framework pasa las tildes a &#NNN; y la página
        // es UTF-8 (así el texto sigue legible en el código fuente y en los tests).
        internal static string H(string texto)
            => (texto ?? string.Empty).Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&#39;");
    }
}
