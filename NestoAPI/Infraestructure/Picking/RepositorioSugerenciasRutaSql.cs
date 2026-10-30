using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Picking
{
    /// <summary>
    /// Sugerencia 575: lectura de los candidatos y cambio de ruta con rastro.
    ///
    /// <para>El rastro (quién y cuándo) va a <c>LogModificaciones</c> (Columna «Ruta», ValorAntiguo «FW -> AT»), la misma
    /// tabla en la que <c>trgCabPedidoVtaUpd</c> ya deja los cambios de ServirJunto, MantenerJunto, CCC y PlazosPago. Con
    /// el script <c>Scripts/Issue575_RegistroCambiosRuta.sql</c> lo deja el trigger <c>trgCabPedidoVtaRuta</c> para
    /// CUALQUIER cambio de ruta (Nesto viejo, SQL, el PUT de la API…); aquí se le pasa el usuario del JWT por
    /// SESSION_CONTEXT para que no quede la cuenta de máquina de la API. Mientras el trigger no exista, la fila la escribe
    /// este mismo lote, así que funciona antes y después de lanzar el script.</para>
    /// </summary>
    public class RepositorioSugerenciasRutaSql : IRepositorioSugerenciasRuta
    {
        private readonly NVEntities db;

        public RepositorioSugerenciasRutaSql(NVEntities db)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public const string NOMBRE_TRIGGER = "trgCabPedidoVtaRuta";

        // {0}: el filtro (pedido o cliente). Las líneas, como las del picking (RellenadorPickingService): pendientes o en
        // curso, de Algete, de la empresa 1 o 3 y sin picking; y con fecha de entrega dentro del horizonte del picking.
        private const string SQL_CANDIDATOS = @"
SELECT c.Empresa, c.Número AS Pedido, c.Fecha AS FechaPedido, c.[Nº Cliente] AS Cliente, c.Contacto, c.Ruta,
       cl.Nombre, cl.CodPostal AS CodigoPostal, cl.DiasEnServir, cl.Población AS Poblacion,
       p.BaseImponible, p.TieneAparatos,
       (SELECT TOP 1 c2.Ruta
          FROM CabPedidoVta c2 WITH (NOLOCK)
         WHERE c2.Empresa IN (@empresaDefecto, @empresaEspejo) AND c2.[Nº Cliente] = c.[Nº Cliente] AND c2.Número <> c.Número
           AND EXISTS (SELECT 1 FROM LinPedidoVta l2 WITH (NOLOCK)
                        WHERE l2.Empresa = c2.Empresa AND l2.Número = c2.Número AND l2.Estado >= @estadoAlbaran AND l2.Almacén = @almacen)
         ORDER BY c2.Número DESC) AS RutaUltimoPedido
  FROM (SELECT l.Empresa, l.Número,
               ISNULL(SUM(CASE WHEN l.TipoLinea = @tipoProducto THEN l.[Base Imponible] ELSE 0 END), 0) AS BaseImponible,
               CAST(MAX(CASE WHEN l.TipoLinea = @tipoProducto AND l.Grupo = @grupoAparatos THEN 1 ELSE 0 END) AS bit) AS TieneAparatos
          FROM LinPedidoVta l WITH (NOLOCK)
         WHERE l.Estado IN (@estadoPendiente, @estadoEnCurso) AND l.Almacén = @almacen AND (l.Picking IS NULL OR l.Picking = 0)
           AND l.Empresa IN (@empresaDefecto, @empresaEspejo) AND l.[Fecha Entrega] < @diaSiguienteAlPicking
           {0}
         GROUP BY l.Empresa, l.Número) p
  JOIN CabPedidoVta c WITH (NOLOCK) ON c.Empresa = p.Empresa AND c.Número = p.Número
  JOIN Clientes cl WITH (NOLOCK) ON cl.Empresa = c.Empresa AND cl.[Nº Cliente] = c.[Nº Cliente] AND cl.Contacto = c.Contacto
 WHERE c.Ruta IN ({1})
 ORDER BY c.Empresa, c.Número";

        public async Task<List<DatosPedidoSugerenciaRuta>> LeerCandidatos(string empresa, int? pedido, string cliente, DateTime fechaPicking)
        {
            var parametros = new List<SqlParameter>
            {
                new SqlParameter("@empresaDefecto", SqlDbType.Char, 3) { Value = Constantes.Empresas.EMPRESA_POR_DEFECTO },
                new SqlParameter("@empresaEspejo", SqlDbType.Char, 3) { Value = Constantes.Empresas.EMPRESA_ESPEJO_POR_DEFECTO },
                new SqlParameter("@almacen", SqlDbType.Char, 3) { Value = Constantes.Productos.ALMACEN_POR_DEFECTO },
                new SqlParameter("@estadoPendiente", SqlDbType.SmallInt) { Value = Constantes.EstadosLineaVenta.PENDIENTE },
                new SqlParameter("@estadoEnCurso", SqlDbType.SmallInt) { Value = Constantes.EstadosLineaVenta.EN_CURSO },
                new SqlParameter("@estadoAlbaran", SqlDbType.SmallInt) { Value = Constantes.EstadosLineaVenta.ALBARAN },
                new SqlParameter("@tipoProducto", SqlDbType.TinyInt) { Value = Constantes.TiposLineaVenta.PRODUCTO },
                new SqlParameter("@grupoAparatos", SqlDbType.Char, 3) { Value = ReglasRutaPropia.GRUPO_APARATOS },
                new SqlParameter("@diaSiguienteAlPicking", SqlDbType.DateTime) { Value = fechaPicking.Date.AddDays(1) }
            };

            string filtro = string.Empty;
            if (pedido.HasValue)
            {
                filtro = "AND l.Empresa = @empresa AND l.Número = @pedido";
                parametros.Add(new SqlParameter("@empresa", SqlDbType.Char, 3) { Value = empresa ?? Constantes.Empresas.EMPRESA_POR_DEFECTO });
                parametros.Add(new SqlParameter("@pedido", SqlDbType.Int) { Value = pedido.Value });
            }
            else if (!string.IsNullOrWhiteSpace(cliente))
            {
                filtro = "AND l.[Nº Cliente] = @cliente";
                parametros.Add(new SqlParameter("@cliente", SqlDbType.VarChar, 10) { Value = cliente.Trim() });
            }

            var rutas = new List<string>();
            for (int i = 0; i < ReglasRutaPropia.RUTAS_AGENCIA_MADRID.Count; i++)
            {
                rutas.Add("@ruta" + i);
                parametros.Add(new SqlParameter("@ruta" + i, SqlDbType.Char, 3) { Value = ReglasRutaPropia.RUTAS_AGENCIA_MADRID[i] });
            }

            string sql = string.Format(SQL_CANDIDATOS, filtro, string.Join(", ", rutas));
            return await db.Database.SqlQuery<DatosPedidoSugerenciaRuta>(sql, parametros.Cast<object>().ToArray())
                .ToListAsync().ConfigureAwait(false);
        }

        // Un solo lote (una sola sesión): el usuario va por SESSION_CONTEXT al trigger; si el trigger aún no existe, la
        // fila de LogModificaciones la escribe el propio lote. XACT_ABORT: o se cambia y queda rastro, o nada.
        internal const string SQL_CAMBIAR_RUTA = @"
SET NOCOUNT ON;
SET XACT_ABORT ON;
DECLARE @filas int;
EXEC sp_set_session_context N'UsuarioNesto', @usuario;
EXEC sp_set_session_context N'FormularioNesto', @formulario;
BEGIN TRANSACTION;
UPDATE CabPedidoVta SET Ruta = @rutaNueva, Usuario = @usuario, [Fecha Modificación] = GETDATE()
 WHERE Empresa = @empresa AND Número = @pedido AND Ruta = @rutaAnterior;
SET @filas = @@ROWCOUNT;
IF @filas > 0 AND OBJECT_ID(N'dbo." + NOMBRE_TRIGGER + @"', N'TR') IS NULL
    INSERT INTO LogModificaciones (Empresa, Formulario, Procedimiento, Tabla, Numero, Columna, Fecha, ValorAntiguo, Usuario)
    VALUES (@empresa, @formulario, 'Actualizar', 'CabPedidoVta', CAST(@pedido AS varchar(50)), 'Ruta', GETDATE(),
            RTRIM(@rutaAnterior) + ' -> ' + RTRIM(@rutaNueva), @usuario);
COMMIT TRANSACTION;
EXEC sp_set_session_context N'UsuarioNesto', NULL;
EXEC sp_set_session_context N'FormularioNesto', NULL;
SELECT @filas;";

        public async Task<int> CambiarRuta(string empresa, int pedido, string rutaAnterior, string rutaNueva, string usuario, string formulario)
        {
            object[] parametros =
            {
                new SqlParameter("@empresa", SqlDbType.Char, 3) { Value = empresa },
                new SqlParameter("@pedido", SqlDbType.Int) { Value = pedido },
                new SqlParameter("@rutaAnterior", SqlDbType.Char, 3) { Value = rutaAnterior },
                new SqlParameter("@rutaNueva", SqlDbType.Char, 3) { Value = rutaNueva },
                new SqlParameter("@usuario", SqlDbType.VarChar, 30) { Value = usuario },
                new SqlParameter("@formulario", SqlDbType.VarChar, 50) { Value = formulario }
            };
            List<int> filas = await db.Database.SqlQuery<int>(SQL_CAMBIAR_RUTA, parametros).ToListAsync().ConfigureAwait(false);
            return filas.FirstOrDefault();
        }
    }
}
