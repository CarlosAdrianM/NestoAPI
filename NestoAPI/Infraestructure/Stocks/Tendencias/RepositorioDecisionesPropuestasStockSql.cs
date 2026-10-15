using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Net;

namespace NestoAPI.Infraestructure.Stocks.Tendencias
{
    /// <summary>
    /// NestoAPI#631: <see cref="IRepositorioDecisionesPropuestasStock"/> con SQL a mano (sin tocar el EDMX) para PropuestasStock
    /// y PropuestasStockDecisiones, y con EF para ControlesStock y Productos (mismas entidades que PUT api/ControlesStock).
    /// Todo lo de una decisión va en UNA transacción: si salta un trigger (trgProductosUpd no deja sacar del estado 0 un
    /// producto con máximos), no queda nada a medias.
    /// </summary>
    public class RepositorioDecisionesPropuestasStockSql : IRepositorioDecisionesPropuestasStock
    {
        internal const int TIMEOUT_SEGUNDOS = 60;
        internal const int LONGITUD_CAMBIOS = 1000;

        internal const string SQL_EXISTE_ESQUEMA = @"
SELECT CASE WHEN COL_LENGTH('dbo.PropuestasStock', 'SilenciadaHasta') IS NOT NULL
             AND OBJECT_ID('dbo.PropuestasStockDecisiones', 'U') IS NOT NULL THEN 1 ELSE 0 END";

        /// <summary>
        /// El estado de cada fila. Una fila sin decidir con otra más reciente del mismo producto y almacén está «Superada»; una
        /// sin decidir está «Silenciada» si un silencio de ese mes o de antes llega al menos al día en que se calculó (el 1 del
        /// mes siguiente al de la fila).
        /// </summary>
        internal const string SQL_ESTADO = @"
CASE
    WHEN p.Decision = 'Aplicar' THEN 'Aplicada'
    WHEN p.Decision = 'Rechazar' THEN 'Rechazada'
    WHEN p.Decision = 'Silenciar' THEN 'Silenciada'
    WHEN p.Accion IN ('Mantener', 'Nada') THEN 'SinAccion'
    WHEN mr.MesMasReciente IS NOT NULL THEN 'Superada'
    WHEN s.Id IS NOT NULL THEN 'Silenciada'
    WHEN p.Decision = 'Posponer' THEN 'Pospuesta'
    ELSE 'Pendiente'
END";

        internal const string SQL_FILAS = @"
SELECT p.Id, p.Mes, RTRIM(p.Producto) AS Producto, p.Nombre, p.Familia, p.Grupo, p.Proveedor, p.EstadoProducto, RTRIM(p.Almacen) AS Almacen, p.Fase,
       p.MesesDesdePrimeraVenta, p.Stock, p.PrecioMedio, p.Ventas12Meses, p.VentaMedia6Meses, p.VentaMediaPonderada, p.VentaMensualReferencia,
       p.FactorEstacional, p.ConEstacionalidad, p.DiasEnServir, p.DiasEnServirSupuestos, p.DiasSeguridad, p.Multiplos, p.CoberturaObjetivoDias,
       p.StockMaximoActual, p.StockMinimoActual, p.Formula, p.CoberturaMeses, p.CoberturaAlcanzableDias, p.PendienteRelativa, p.RatioTrimestre,
       p.AnioPico, p.PorcentajeDelPico, p.AniosDesdePico, p.Tendencia, p.Velocidad, p.TasaRecompra, p.DemandaNoServida, p.PedidoCompraAbierto,
       p.ExcesoUnidades, p.ExcesoValorado, p.Accion, p.StockMaximoPropuesto, p.StockMinimoPropuesto, p.Explicacion, p.EntradasJson,
       p.FechaCalculo, p.Decision, p.DecisionUsuario, p.DecisionFecha, p.DecisionMotivo,
       p.MotivoSilencio, p.SilenciadaHasta, p.CanalLiquidacion, p.StockMaximoAnterior, p.StockMinimoAnterior, p.StockMaximoAplicado,
       p.StockMinimoAplicado, p.EstadoAnterior, p.EstadoAplicado,
       s.Id AS SilencioPropuestaId, s.SilenciadaHasta AS SilencioHasta, s.MotivoSilencio AS SilencioMotivo, mr.MesMasReciente,
       " + SQL_ESTADO + @" AS Estado
FROM dbo.PropuestasStock p WITH (NOLOCK)
OUTER APPLY (SELECT MAX(n.Mes) AS MesMasReciente FROM dbo.PropuestasStock n WITH (NOLOCK)
             WHERE n.Producto = p.Producto AND n.Almacen = p.Almacen AND n.Mes > p.Mes) mr
OUTER APPLY (SELECT TOP 1 x.Id, x.SilenciadaHasta, x.MotivoSilencio FROM dbo.PropuestasStock x WITH (NOLOCK)
             WHERE x.Producto = p.Producto AND x.Almacen = p.Almacen AND x.Decision = 'Silenciar' AND x.Mes <= p.Mes
               AND x.SilenciadaHasta >= DATEADD(MONTH, 1, p.Mes)
             ORDER BY x.SilenciadaHasta DESC) s";

        internal const string SQL_LEER_UNA = "SELECT * FROM (" + SQL_FILAS + @"
WHERE p.Id = @id) f";

        internal const string SQL_LEER = @"
SELECT TOP (@top) * FROM (" + SQL_FILAS + @"
WHERE p.Mes = ISNULL(@mes, (SELECT MAX(Mes) FROM dbo.PropuestasStock WITH (NOLOCK)))) f
WHERE @estado IS NULL OR f.Estado = @estado
ORDER BY f.ExcesoValorado DESC, f.Producto";

        internal const string SQL_RESUMEN = @"
SELECT f.Mes, f.Estado, COUNT(*) AS Propuestas, CAST(ISNULL(SUM(f.ExcesoValorado), 0) AS decimal(18, 2)) AS ExcesoValorado FROM (" + SQL_FILAS + @"
WHERE p.Mes = ISNULL(@mes, (SELECT MAX(Mes) FROM dbo.PropuestasStock WITH (NOLOCK)))) f
GROUP BY f.Mes, f.Estado
ORDER BY f.Estado";

        internal const string SQL_ESTADO_PRODUCTO = "SELECT Estado FROM Productos WITH (NOLOCK) WHERE Empresa = @empresa AND Número = @producto";

        /// <summary>Solo si sigue en el estado que se leyó.</summary>
        internal const string SQL_ESTADO_NUEVO = @"
UPDATE Productos SET Estado = @estado, Usuario = @usuario, [Fecha Modificación] = @fecha
WHERE Empresa = @empresa AND Número = @producto AND Estado = @estadoAnterior";

        internal const string SQL_CONTROLES = @"
SELECT RTRIM(Almacén) AS Almacen, CAST(StockMáximo AS int) AS StockMaximo, CAST(StockMínimo AS int) AS StockMinimo
FROM ControlesStock WITH (NOLOCK) WHERE Empresa = @empresa AND Número = @producto";

        /// <summary>Solo si nadie la ha decidido desde que se leyó (misma decisión y misma fecha).</summary>
        internal const string SQL_GUARDAR = @"
UPDATE dbo.PropuestasStock SET
    Decision = @decision, DecisionUsuario = @usuario, DecisionFecha = @fecha, DecisionMotivo = @motivo,
    MotivoSilencio = @motivoSilencio, SilenciadaHasta = @silenciadaHasta, CanalLiquidacion = @canal,
    StockMaximoAnterior = @maximoAnterior, StockMinimoAnterior = @minimoAnterior, StockMaximoAplicado = @maximoAplicado,
    StockMinimoAplicado = @minimoAplicado, EstadoAnterior = @estadoAnterior, EstadoAplicado = @estadoAplicado
WHERE Id = @id
  AND ((Decision IS NULL AND @decisionAnterior IS NULL) OR Decision = @decisionAnterior)
  AND ((DecisionFecha IS NULL AND @fechaAnterior IS NULL) OR DecisionFecha = @fechaAnterior)";

        internal const string SQL_HISTORICO = @"
INSERT INTO dbo.PropuestasStockDecisiones (PropuestaId, Mes, Producto, Almacen, Decision, Usuario, Fecha, Motivo, MotivoSilencio,
    SilenciadaHasta, CanalLiquidacion, Cambios)
VALUES (@id, @mes, @producto, @almacen, @decision, @usuario, @fecha, @motivo, @motivoSilencio, @silenciadaHasta, @canal, @cambios)";

        private readonly Func<NVEntities> crearContexto;

        public RepositorioDecisionesPropuestasStockSql() : this(null)
        {
        }

        internal RepositorioDecisionesPropuestasStockSql(Func<NVEntities> crearContexto)
        {
            this.crearContexto = crearContexto ?? (() => new NVEntities());
        }

        private NVEntities Contexto()
        {
            NVEntities db = crearContexto();
            db.Configuration.LazyLoadingEnabled = false;
            db.Configuration.ProxyCreationEnabled = false;
            db.Database.CommandTimeout = TIMEOUT_SEGUNDOS;
            return db;
        }

        private static SqlParameter Mes(DateTime? mes) => new SqlParameter("@mes", SqlDbType.Date) { Value = mes.HasValue ? (object)mes.Value : DBNull.Value };
        private static SqlParameter Empresa() => new SqlParameter("@empresa", SqlDbType.Char, 3) { Value = Constantes.Empresas.EMPRESA_POR_DEFECTO };
        private static SqlParameter Producto(string producto) => new SqlParameter("@producto", SqlDbType.VarChar, 15) { Value = producto };
        private static object N(object valor) => valor ?? DBNull.Value;

        public bool ExisteEsquema()
        {
            using (NVEntities db = Contexto())
            {
                return db.Database.SqlQuery<int>(SQL_EXISTE_ESQUEMA).Single() == 1;
            }
        }

        public PropuestaStockConDecision LeerPropuesta(int id)
        {
            using (NVEntities db = Contexto())
            {
                return db.Database.SqlQuery<PropuestaStockConDecision>(SQL_LEER_UNA, new SqlParameter("@id", SqlDbType.Int) { Value = id }).SingleOrDefault();
            }
        }

        public List<PropuestaStockConDecision> LeerPropuestas(DateTime? mes, string estado, int top)
        {
            using (NVEntities db = Contexto())
            {
                return db.Database.SqlQuery<PropuestaStockConDecision>(SQL_LEER,
                    new SqlParameter("@top", SqlDbType.Int) { Value = top },
                    Mes(mes),
                    new SqlParameter("@estado", SqlDbType.VarChar, 20) { Value = N(estado) }).ToList();
            }
        }

        public List<ResumenEstadoPropuestas> Resumen(DateTime? mes)
        {
            using (NVEntities db = Contexto())
            {
                return db.Database.SqlQuery<ResumenEstadoPropuestas>(SQL_RESUMEN, Mes(mes)).ToList();
            }
        }

        public short? LeerEstadoProducto(string producto)
        {
            using (NVEntities db = Contexto())
            {
                return db.Database.SqlQuery<short?>(SQL_ESTADO_PRODUCTO, Empresa(), Producto(producto)).FirstOrDefault();
            }
        }

        public List<ControlStockActual> LeerControlesStock(string producto)
        {
            using (NVEntities db = Contexto())
            {
                return db.Database.SqlQuery<ControlStockActual>(SQL_CONTROLES, Empresa(), Producto(producto)).ToList();
            }
        }

        /// <summary>
        /// Solo la fila «(defecto)», a propósito (como PermisoRellenarReposicionManual): ParametrosUsuarioController.LeerParametro
        /// copiaría el valor a una fila del usuario y cada uno tendría su propia lista.
        /// </summary>
        public string LeerParametroMotivosSilencio()
        {
            using (NVEntities db = Contexto())
            {
                string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO;
                return db.ParametrosUsuario
                    .Where(p => p.Empresa == empresa && p.Usuario == USUARIO_GENERAL && p.Clave == MotivosSilencio.CLAVE)
                    .Select(p => p.Valor)
                    .FirstOrDefault();
            }
        }

        private const string USUARIO_GENERAL = "(defecto)";

        public void GuardarDecision(RegistroDecisionPropuesta r)
        {
            if (r == null)
            {
                throw new ArgumentNullException(nameof(r));
            }
            string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO;
            using (NVEntities db = Contexto())
            using (DbContextTransaction transaccion = db.Database.BeginTransaction())
            {
                int filas = db.Database.ExecuteSqlCommand(SQL_GUARDAR,
                    new SqlParameter("@id", SqlDbType.Int) { Value = r.PropuestaId },
                    new SqlParameter("@decision", SqlDbType.VarChar, 30) { Value = r.Decision },
                    new SqlParameter("@usuario", SqlDbType.VarChar, 50) { Value = r.Usuario },
                    new SqlParameter("@fecha", SqlDbType.DateTime) { Value = r.Fecha },
                    new SqlParameter("@motivo", SqlDbType.NVarChar, 500) { Value = N(r.Motivo) },
                    new SqlParameter("@motivoSilencio", SqlDbType.VarChar, 20) { Value = N(r.MotivoSilencio) },
                    new SqlParameter("@silenciadaHasta", SqlDbType.Date) { Value = N(r.SilenciadaHasta) },
                    new SqlParameter("@canal", SqlDbType.VarChar, 20) { Value = N(r.CanalLiquidacion) },
                    new SqlParameter("@maximoAnterior", SqlDbType.Int) { Value = N(r.StockMaximoAnterior) },
                    new SqlParameter("@minimoAnterior", SqlDbType.Int) { Value = N(r.StockMinimoAnterior) },
                    new SqlParameter("@maximoAplicado", SqlDbType.Int) { Value = N(r.StockMaximoAplicado) },
                    new SqlParameter("@minimoAplicado", SqlDbType.Int) { Value = N(r.StockMinimoAplicado) },
                    new SqlParameter("@estadoAnterior", SqlDbType.SmallInt) { Value = N(r.EstadoAnterior) },
                    new SqlParameter("@estadoAplicado", SqlDbType.SmallInt) { Value = N(r.EstadoAplicado) },
                    new SqlParameter("@decisionAnterior", SqlDbType.VarChar, 30) { Value = N(r.DecisionAnterior) },
                    new SqlParameter("@fechaAnterior", SqlDbType.DateTime) { Value = N(r.DecisionFechaAnterior) });
                if (filas != 1)
                {
                    throw new DecisionPropuestaStockException(HttpStatusCode.Conflict,
                        "Otra persona acaba de decidir esta propuesta. Vuelva a cargarla para ver qué se ha hecho.");
                }

                // Primero los controles y luego el estado: trgProductosUpd mira los máximos al cambiar el estado.
                if (r.CambiosControlStock.Count > 0)
                {
                    foreach (CambioControlStock cambio in r.CambiosControlStock)
                    {
                        ControlStock control = db.ControlesStocks.SingleOrDefault(c => c.Empresa == empresa && c.Almacén == cambio.Almacen && c.Número == r.Producto);
                        if (control == null)
                        {
                            if (cambio.Existia)
                            {
                                throw new DecisionPropuestaStockException(HttpStatusCode.Conflict,
                                    $"El control de stock de {cambio.Almacen} del producto {r.Producto} se acaba de borrar. Vuelva a cargar la propuesta.");
                            }
                            _ = db.ControlesStocks.Add(ControlesStockEscritura.Nuevo(empresa, cambio.Almacen, r.Producto,
                                (short)cambio.MinimoNuevo, (short)cambio.MaximoNuevo, (short)Math.Min(short.MaxValue, cambio.Multiplos), r.Usuario, r.Fecha));
                        }
                        else
                        {
                            if (cambio.Existia && (control.StockMáximo != cambio.MaximoAnterior || control.StockMínimo != cambio.MinimoAnterior))
                            {
                                throw new DecisionPropuestaStockException(HttpStatusCode.Conflict,
                                    $"El máximo o el mínimo de {cambio.Almacen} del producto {r.Producto} acaba de cambiar ({control.StockMáximo}/{control.StockMínimo}). Vuelva a cargar la propuesta.");
                            }
                            ControlesStockEscritura.CambiarMaximoMinimo(control, (short)cambio.MinimoNuevo, (short)cambio.MaximoNuevo, r.Usuario, r.Fecha);
                        }
                    }
                    _ = db.SaveChanges();
                }

                if (r.EstadoAplicado.HasValue)
                {
                    // SQL a mano y no EF: así solo se tocan estas tres columnas (y EF no valida la ficha entera, que en datos
                    // antiguos puede no cumplir las longitudes del EDMX). El trigger apunta el estado anterior en logmodificaciones.
                    int productos = db.Database.ExecuteSqlCommand(SQL_ESTADO_NUEVO,
                        Empresa(),
                        Producto(r.Producto),
                        new SqlParameter("@estado", SqlDbType.SmallInt) { Value = r.EstadoAplicado.Value },
                        new SqlParameter("@estadoAnterior", SqlDbType.SmallInt) { Value = N(r.EstadoAnterior) },
                        new SqlParameter("@usuario", SqlDbType.VarChar, ControlesStockEscritura.LONGITUD_USUARIO) { Value = ControlesStockEscritura.Usuario(r.Usuario) },
                        new SqlParameter("@fecha", SqlDbType.DateTime) { Value = r.Fecha });
                    if (productos != 1)
                    {
                        throw new DecisionPropuestaStockException(HttpStatusCode.Conflict,
                            $"El estado del producto {r.Producto} acaba de cambiar. Vuelva a cargar la propuesta.");
                    }
                }

                _ = db.Database.ExecuteSqlCommand(SQL_HISTORICO,
                    new SqlParameter("@id", SqlDbType.Int) { Value = r.PropuestaId },
                    Mes(r.Mes),
                    Producto(r.Producto),
                    new SqlParameter("@almacen", SqlDbType.VarChar, 3) { Value = r.Almacen },
                    new SqlParameter("@decision", SqlDbType.VarChar, 30) { Value = r.Decision },
                    new SqlParameter("@usuario", SqlDbType.VarChar, 50) { Value = r.Usuario },
                    new SqlParameter("@fecha", SqlDbType.DateTime) { Value = r.Fecha },
                    new SqlParameter("@motivo", SqlDbType.NVarChar, 500) { Value = N(r.Motivo) },
                    new SqlParameter("@motivoSilencio", SqlDbType.VarChar, 20) { Value = N(r.MotivoSilencio) },
                    new SqlParameter("@silenciadaHasta", SqlDbType.Date) { Value = N(r.SilenciadaHasta) },
                    new SqlParameter("@canal", SqlDbType.VarChar, 20) { Value = N(r.CanalLiquidacion) },
                    new SqlParameter("@cambios", SqlDbType.NVarChar, LONGITUD_CAMBIOS)
                    {
                        Value = N(r.Cambios == null || r.Cambios.Length <= LONGITUD_CAMBIOS ? r.Cambios : r.Cambios.Substring(0, LONGITUD_CAMBIOS))
                    });

                transaccion.Commit();
            }
        }
    }
}
