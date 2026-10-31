using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using NestoAPI.Infraestructure.NotasEntrega;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Models;
using NestoAPI.Models.Agencias;
using NestoAPI.Models.PedidosVenta;

namespace NestoAPI.Infraestructure.Agencias.RepartoPropio
{
    /// <summary>
    /// NestoAPI#759 (corte 3 de #755, ajustes de Carlos del 30/10/26): lo COMÚN a todas las paradas del reparto propio, sean un
    /// envío de la agencia 14 (envío normal, nota de entrega o nota de recogida) o una parada de reposición a una tienda:
    /// <list type="bullet">
    /// <item>Parada = destino. Es del repartidor que la carga escaneando un código suyo (el de la última carga: con «Reasignar»
    /// pasa al nuevo). Si otro escanea un bulto de una parada que ya tiene dueño, 409 <see cref="ServicioRepartoPropio.CODIGO_ENVIO_DE_OTRO"/>
    /// con «Este bulto no es suyo: es de la parada de X».</item>
    /// <item>«Mis paradas» (soloMios) = las suyas + las que no ha cargado nadie (para poder cargarlas). Sin soloMios, todas.</item>
    /// <item>El orden es de cada repartidor sobre SUS paradas (o las de nadie): uno por parada, último gana por FechaHora.</item>
    /// <item>La lista sale por Orden (nulos al final), fecha y número.</item>
    /// </list>
    /// Las diferencias (dónde se guarda la carga, qué es un bulto, cómo se entrega) las ponen ServicioRepartoPropio (envíos) y
    /// <see cref="IRepositorioParadasReposicion"/> (reposiciones).
    /// </summary>
    public static class NucleoParadasReparto
    {
        /// <summary>Quién lleva la parada: el usuario de la última carga (por Id). Null si nadie la ha cargado.</summary>
        public static string Propietario<T>(IEnumerable<T> cargas, Func<T, long> id, Func<T, string> usuario)
            => (cargas ?? Enumerable.Empty<T>()).OrderBy(id).Select(usuario).LastOrDefault()?.Trim();

        /// <summary>Bultos distintos (por <paramref name="bulto"/>) que ha cargado <paramref name="propietario"/> (0 sin propietario).</summary>
        public static int BultosCargados<T, TBulto>(IEnumerable<T> cargas, string propietario, Func<T, string> usuario, Func<T, TBulto> bulto)
            => propietario == null ? 0 : (cargas ?? Enumerable.Empty<T>()).Where(c => ServicioRepartoPropio.MismoUsuario(usuario(c), propietario)).Select(bulto).Distinct().Count();

        /// <summary>La parada tiene dueño y no es <paramref name="usuario"/>.</summary>
        public static bool EsDeOtro(string propietario, string usuario)
            => propietario != null && !ServicioRepartoPropio.MismoUsuario(propietario, usuario);

        /// <summary>Con soloMios: las del usuario y las de nadie. Sin soloMios: todas.</summary>
        public static bool Visible(string repartidor, string usuario, bool soloMios)
            => !soloMios || string.IsNullOrWhiteSpace(repartidor) || ServicioRepartoPropio.MismoUsuario(repartidor, usuario);

        public static string MensajeDeOtro(string propietario)
            => $"Este bulto no es suyo: es de la parada de {ServicioRepartoPropio.NombreRepartidor(propietario)}. Si se la lleva usted, confírmelo para reasignársela.";

        /// <summary>El 409 de una parada de otro repartidor (error.code ENVIO_DE_OTRO_REPARTIDOR y error.details.repartidor).</summary>
        public static ResultadoReparto<T> ConflictoDeOtro<T>(string propietario)
            => new ResultadoReparto<T>
            {
                Tipo = TipoResultadoReparto.Conflicto,
                Mensaje = MensajeDeOtro(propietario),
                Codigo = ServicioRepartoPropio.CODIGO_ENVIO_DE_OTRO,
                Detalles = new { repartidor = ServicioRepartoPropio.NombreRepartidor(propietario) }
            };

        /// <summary>Por Orden (nulos al final), fecha de la parada y número (las de reposición, por su Id).</summary>
        public static List<EnvioRepartoDTO> Ordenar(IEnumerable<EnvioRepartoDTO> paradas)
            => (paradas ?? Enumerable.Empty<EnvioRepartoDTO>())
                .OrderBy(p => p.Orden.HasValue ? 0 : 1)
                .ThenBy(p => p.Orden ?? 0)
                .ThenBy(p => p.FechaParada)
                .ThenBy(p => Math.Abs(p.EnvioId))
                .ToList();

        /// <summary>Las de reposición van con el EnvioId negativo: −Id de RepartoParadasReposicion.</summary>
        public static bool EsReposicion(int envioId) => envioId < 0;

        public static int IdReposicion(int envioId) => -envioId;

        public static int EnvioIdReposicion(int parada) => -parada;
    }

    /// <summary>NestoAPI#759: de qué conexión y dispositivo viene la petición (para que el aviso de SignalR no le llegue a ella misma).</summary>
    public class OrigenPeticionReparto
    {
        /// <summary>Cabecera X-Conexion-SignalR.</summary>
        public string Conexion { get; set; }
        /// <summary>Cabecera X-Dispositivo.</summary>
        public string Dispositivo { get; set; }
    }

    /// <summary>NestoAPI#759: avisar a las demás PDA del reparto de que algo ha cambiado (AlmacenHub, grupo «Reparto»). Nunca lanza.</summary>
    public interface IAvisosReparto
    {
        void RepartoCambiado(string usuario, string motivo, DateTime fechaHora, OrigenPeticionReparto origen);
    }

    // ==================================================================== notas de las paradas

    /// <summary>NestoAPI#759: lo que la lista necesita de una nota de entrega (pedido con NotaEntrega = 1 de la empresa 1).</summary>
    public class NotaParadaReparto
    {
        public int Pedido { get; set; }
        /// <summary>«Entrega» o «Recogida» (sin fila en NotasEntregaCabecera, Entrega).</summary>
        public string Tipo { get; set; } = TiposNotaEntrega.ENTREGA;
        public int? NumeroNota { get; set; }
        /// <summary>Las líneas de texto, recortadas, sin las vacías y separadas por \n. Null si no tiene.</summary>
        public string Texto { get; set; }

        public bool EsRecogida => Tipo == TiposNotaEntrega.RECOGIDA;
        public string TipoParada => EsRecogida ? TiposParadaReparto.NOTA_RECOGIDA : TiposParadaReparto.NOTA_ENTREGA;
        public string CodigoDocumento => CodigoNotaEntrega.Documento(EsRecogida, Pedido);
    }

    public interface IRepositorioNotasParadaReparto
    {
        /// <summary>Las que son notas de entrega de esos pedidos (empresa 1). Las que no lo son, no vienen.</summary>
        Task<List<NotaParadaReparto>> LeerNotas(IReadOnlyCollection<int> pedidos);
    }

    /// <summary>NestoAPI#759: las notas de una lista de pedidos en pocas consultas (cabecera, tipo, texto y número de nota).</summary>
    public class RepositorioNotasParadaReparto : IRepositorioNotasParadaReparto
    {
        private class FilaTipo
        {
            public int Pedido { get; set; }
            public string Tipo { get; set; }
        }

        private readonly NVEntities db;

        public RepositorioNotasParadaReparto(NVEntities db)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public async Task<List<NotaParadaReparto>> LeerNotas(IReadOnlyCollection<int> pedidos)
        {
            List<int> lista = (pedidos ?? new int[0]).Where(p => p > 0).Distinct().ToList();
            if (lista.Count == 0)
            {
                return new List<NotaParadaReparto>();
            }
            string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO;
            List<int> notas = await db.CabPedidoVtas.AsNoTracking()
                .Where(c => c.Empresa == empresa && lista.Contains(c.Número) && c.NotaEntrega)
                .Select(c => c.Número)
                .ToListAsync().ConfigureAwait(false);
            if (notas.Count == 0)
            {
                return new List<NotaParadaReparto>();
            }

            var tipos = new Dictionary<int, string>();
            if (ColumnasOpcionales.ExisteTabla(db, RepositorioNotasEntregaCabecera.TABLA))
            {
                // Enteros en el texto (sin inyección posible).
                string ids = string.Join(", ", notas.Select(n => n.ToString(CultureInfo.InvariantCulture)));
                List<FilaTipo> filas = await db.Database.SqlQuery<FilaTipo>(
                    $"SELECT Pedido, RTRIM(Tipo) AS Tipo FROM dbo.{RepositorioNotasEntregaCabecera.TABLA} WITH (NOLOCK) WHERE Empresa = @p0 AND Pedido IN ({ids})",
                    empresa).ToListAsync().ConfigureAwait(false);
                foreach (FilaTipo fila in filas)
                {
                    tipos[fila.Pedido] = TiposNotaEntrega.Normalizar(fila.Tipo) ?? TiposNotaEntrega.ENTREGA;
                }
            }

            int texto = Constantes.TiposLineaVenta.TEXTO;
            var textos = await db.LinPedidoVtas.AsNoTracking()
                .Where(l => l.Empresa == empresa && notas.Contains(l.Número) && l.TipoLinea == texto)
                .OrderBy(l => l.Nº_Orden)
                .Select(l => new { l.Número, l.Texto })
                .ToListAsync().ConfigureAwait(false);
            var numeros = await (from l in db.LinPedidoVtas
                                 join n in db.NotasEntregas on l.Nº_Orden equals n.NºOrden
                                 where l.Empresa == empresa && notas.Contains(l.Número)
                                 group n.Numero by l.Número into g
                                 select new { Pedido = g.Key, Numero = g.Max() })
                .ToListAsync().ConfigureAwait(false);

            return notas.Select(n => new NotaParadaReparto
            {
                Pedido = n,
                Tipo = tipos.TryGetValue(n, out string tipo) ? tipo : TiposNotaEntrega.ENTREGA,
                NumeroNota = numeros.Where(x => x.Pedido == n).Select(x => (int?)x.Numero).FirstOrDefault(),
                Texto = Texto(textos.Where(t => t.Número == n).Select(t => t.Texto))
            }).ToList();
        }

        /// <summary>Las líneas de texto recortadas y sin las vacías, separadas por \n; null si no queda ninguna.</summary>
        internal static string Texto(IEnumerable<string> lineas)
        {
            List<string> limpias = (lineas ?? Enumerable.Empty<string>()).Select(t => t?.Trim()).Where(t => !string.IsNullOrEmpty(t)).ToList();
            return limpias.Count == 0 ? null : string.Join("\n", limpias);
        }
    }

    // ==================================================================== orden

    /// <summary>NestoAPI#759: una fila de RepartoPropioOrdenParadas.</summary>
    public class OrdenParadaReparto
    {
        /// <summary>El EnvioId de la parada (negativo en las de reposición).</summary>
        public int ParadaId { get; set; }
        public int Orden { get; set; }
        public string Usuario { get; set; }
        public DateTime FechaHora { get; set; }
    }

    /// <summary>NestoAPI#759: lo justo de un envío para saber si se puede ordenar.</summary>
    public class EstadoEnvioParada
    {
        public int Numero { get; set; }
        public int Agencia { get; set; }
        public short Estado { get; set; }
    }

    public interface IRepositorioOrdenReparto
    {
        /// <summary>¿Existe RepartoPropioOrdenParadas (Scripts/Issue759_ParadasReparto.sql)?</summary>
        bool ScriptAplicado();
        Task<List<OrdenParadaReparto>> Leer(IReadOnlyCollection<int> paradas);
        /// <summary>
        /// Upsert de cada fila, en una transacción, solo si no hay ya una con FechaHora más nueva (último gana). Devuelve las
        /// ParadaId que se han guardado.
        /// </summary>
        Task<HashSet<int>> Guardar(IReadOnlyList<OrdenParadaReparto> filas);
        /// <summary>Agencia y estado de esos envíos (los que existan).</summary>
        Task<List<EstadoEnvioParada>> LeerEstadosEnvios(IReadOnlyCollection<int> envios);
    }

    /// <summary>NestoAPI#759: RepartoPropioOrdenParadas, fuera del EDMX (SQL a mano), como las demás tablas del reparto propio.</summary>
    public class RepositorioOrdenReparto : IRepositorioOrdenReparto
    {
        public const string TABLA = "RepartoPropioOrdenParadas";

        private const string SQL_GUARDAR = @"
SET NOCOUNT ON;
UPDATE dbo." + TABLA + @" SET Orden = @Orden, Usuario = @Usuario, FechaHora = @FechaHora, FechaRegistro = GETDATE()
WHERE ParadaId = @Id AND FechaHora <= @FechaHora;
IF @@ROWCOUNT = 0 AND NOT EXISTS (SELECT 1 FROM dbo." + TABLA + @" WITH (UPDLOCK, HOLDLOCK) WHERE ParadaId = @Id)
    INSERT INTO dbo." + TABLA + @" (ParadaId, Orden, Usuario, FechaHora, FechaRegistro) VALUES (@Id, @Orden, @Usuario, @FechaHora, GETDATE());
SELECT CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo." + TABLA + @" WHERE ParadaId = @Id AND FechaHora = @FechaHora AND Orden = @Orden) THEN 1 ELSE 0 END AS bit);";

        /// <summary>El reintento tras perder la carrera del INSERT: la fila ya existe, solo queda el UPDATE (último gana por FechaHora).</summary>
        private const string SQL_ACTUALIZAR = @"
SET NOCOUNT ON;
UPDATE dbo." + TABLA + @" SET Orden = @Orden, Usuario = @Usuario, FechaHora = @FechaHora, FechaRegistro = GETDATE()
WHERE ParadaId = @Id AND FechaHora <= @FechaHora;
SELECT CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo." + TABLA + @" WHERE ParadaId = @Id AND FechaHora = @FechaHora AND Orden = @Orden) THEN 1 ELSE 0 END AS bit);";

        private readonly NVEntities db;

        public RepositorioOrdenReparto(NVEntities db)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public bool ScriptAplicado() => ColumnasOpcionales.ExisteTabla(db, TABLA);

        public Task<List<OrdenParadaReparto>> Leer(IReadOnlyCollection<int> paradas)
        {
            if (paradas == null || paradas.Count == 0)
            {
                return Task.FromResult(new List<OrdenParadaReparto>());
            }
            string lista = string.Join(", ", paradas.Distinct().Select(p => p.ToString(CultureInfo.InvariantCulture)));
            return db.Database.SqlQuery<OrdenParadaReparto>(
                $"SELECT ParadaId, Orden, RTRIM(Usuario) AS Usuario, FechaHora FROM dbo.{TABLA} WITH (NOLOCK) WHERE ParadaId IN ({lista})").ToListAsync();
        }

        public async Task<HashSet<int>> Guardar(IReadOnlyList<OrdenParadaReparto> filas)
        {
            var guardadas = new HashSet<int>();
            if (filas == null || filas.Count == 0)
            {
                return guardadas;
            }
            using (DbContextTransaction transaccion = db.Database.BeginTransaction())
            {
                try
                {
                    foreach (OrdenParadaReparto fila in filas)
                    {
                        bool guardada = await GuardarConReintento(async soloActualizar => (await db.Database.SqlQuery<bool>(soloActualizar ? SQL_ACTUALIZAR : SQL_GUARDAR,
                            new SqlParameter("@Id", SqlDbType.Int) { Value = fila.ParadaId },
                            new SqlParameter("@Orden", SqlDbType.Int) { Value = fila.Orden },
                            new SqlParameter("@Usuario", SqlDbType.VarChar, 50) { Value = Recortar(fila.Usuario, 50) ?? string.Empty },
                            new SqlParameter("@FechaHora", SqlDbType.DateTime) { Value = fila.FechaHora }).ToListAsync().ConfigureAwait(false)).FirstOrDefault())
                            .ConfigureAwait(false);
                        if (guardada)
                        {
                            _ = guardadas.Add(fila.ParadaId);
                        }
                    }
                    transaccion.Commit();
                }
                catch (Exception)
                {
                    transaccion.RollbackSeguro();
                    throw;
                }
            }
            return guardadas;
        }

        /// <summary>
        /// Revisión 30/10: dos PDA guardan a la vez el orden de una parada que aún no tiene fila; las dos ven que no existe y la
        /// segunda choca con la PK (2627/2601) al insertar. Un error de clave duplicada solo termina la sentencia (la transacción
        /// sigue viva), así que se reintenta UNA vez solo el UPDATE: último gana por FechaHora, como siempre.
        /// </summary>
        /// <param name="ejecutar">Ejecuta el upsert (false) o solo el UPDATE (true) y dice si la fila ha quedado con lo suyo.</param>
        internal static async Task<bool> GuardarConReintento(Func<bool, Task<bool>> ejecutar)
        {
            try
            {
                return await ejecutar(false).ConfigureAwait(false);
            }
            catch (Exception ex) when (EsClaveDuplicada(ex))
            {
                return await ejecutar(true).ConfigureAwait(false);
            }
        }

        private static bool EsClaveDuplicada(Exception ex)
        {
            for (Exception e = ex; e != null; e = e.InnerException)
            {
                if (e is SqlException sql && (sql.Number == 2627 || sql.Number == 2601))
                {
                    return true;
                }
            }
            return false;
        }

        public Task<List<EstadoEnvioParada>> LeerEstadosEnvios(IReadOnlyCollection<int> envios)
        {
            List<int> lista = (envios ?? new int[0]).Where(e => e > 0).Distinct().ToList();
            if (lista.Count == 0)
            {
                return Task.FromResult(new List<EstadoEnvioParada>());
            }
            return db.EnviosAgencias.AsNoTracking()
                .Where(e => lista.Contains(e.Numero))
                .Select(e => new EstadoEnvioParada { Numero = e.Numero, Agencia = e.Agencia, Estado = e.Estado })
                .ToListAsync();
        }

        private static string Recortar(string valor, int maximo) => valor == null || valor.Length <= maximo ? valor : valor.Substring(0, maximo);
    }

    // ==================================================================== dependencias

    /// <summary>
    /// NestoAPI#759: lo que necesitan las paradas (notas, orden, reposiciones y avisos), juntas para no alargar más el constructor
    /// de <see cref="ServicioRepartoPropio"/>. Cualquiera puede faltar (null): esa parte se comporta como antes del corte 3.
    /// </summary>
    public class DependenciasParadasReparto
    {
        /// <summary>Resolver NE/NR al envío de su parada (corte 2).</summary>
        public IServicioParadasNotaEntrega Notas { get; set; }
        /// <summary>Tipo, texto y número de las notas de la lista.</summary>
        public IRepositorioNotasParadaReparto NotasParada { get; set; }
        public IRepositorioOrdenReparto Orden { get; set; }
        public IRepositorioParadasReposicion Reposiciones { get; set; }
        /// <summary>Las etiquetas R{traspaso}-{N} (ReposicionesBultos, NestoAPI#645).</summary>
        public IRepositorioBultosReposicion BultosReposicion { get; set; }
        public IAvisosReparto Avisos { get; set; }
        /// <summary>NestoAPI#767: la caché de coordenadas (DireccionesCoordenadas) y pedir que se rellene. Null: sin coordenadas.</summary>
        public ICoordenadasParadas Coordenadas { get; set; }
        /// <summary>NestoAPI#767: el orden sugerido (OSRM con la heurística de respaldo). Null: solo la heurística.</summary>
        public IPlanificadorRecorrido Recorrido { get; set; }
        /// <summary>NestoAPI#763: el horario preferible de los clientes de las paradas. Null: sin horarios (como antes).</summary>
        public IHorariosParadas Horarios { get; set; }
        /// <summary>
        /// NestoAPI#769: la matriz de tiempos (OSRM /table) para reordenar por el horario preferible; si no responde, la línea
        /// recta. Null: no se reordena (solo se avisa, como en #763).
        /// </summary>
        public IMatrizTiempos Tiempos { get; set; }
        /// <summary>NestoAPI#772: lo que lleva cada furgoneta (RepartoBultosMovimientos, RepartoJornadas). Null: como antes.</summary>
        public IRepositorioFurgoneta Furgoneta { get; set; }
        /// <summary>NestoAPI#772: dejar «Recogida» la reposición de una tienda con el último bulto. Null: no se marca.</summary>
        public IRecogidaReposicionesReparto RecogidaReposiciones { get; set; }
        /// <summary>NestoAPI#772: «haCambiado» al grupo «Reposiciones:{tienda}» (Nesto). Null: sin aviso.</summary>
        public Notificaciones.IAvisosAlmacen AvisosAlmacen { get; set; }
        /// <summary>NestoAPI#772: la campana de Nesto cuando un repartidor termina con bultos en la furgoneta. Null: sin aviso.</summary>
        public IAvisosFurgoneta AvisosFurgoneta { get; set; }

        public static DependenciasParadasReparto DeLaBaseDeDatos(NVEntities db, IAvisosReparto avisos)
            => new DependenciasParadasReparto
            {
                Furgoneta = new RepositorioFurgoneta(db),
                RecogidaReposiciones = new RecogidaReposicionesReparto(db),
                AvisosAlmacen = avisos as Notificaciones.IAvisosAlmacen,
                AvisosFurgoneta = new AvisosFurgonetaSinDescargar(),
                Horarios = new HorariosParadas(new global::NestoAPI.Infraestructure.Clientes.ServicioHorarioPreferible(
                    new global::NestoAPI.Infraestructure.Clientes.RepositorioHorarioPreferible(db))),
                Coordenadas = new ServicioGeocodificacion(new RepositorioCoordenadas(db), pedir: GeocodificacionRepartoJobsService.EncolarParadas),
                Recorrido = new PlanificadorRecorrido(new MotorRutasOsrm()),
                Tiempos = new MatrizTiemposOsrm(),
                Notas = new ServicioParadasNotaEntrega(db),
                NotasParada = new RepositorioNotasParadaReparto(db),
                Orden = new RepositorioOrdenReparto(db),
                Reposiciones = new RepositorioParadasReposicion(db),
                BultosReposicion = new RepositorioBultosReposicionSql(db),
                Avisos = avisos
            };
    }
}
