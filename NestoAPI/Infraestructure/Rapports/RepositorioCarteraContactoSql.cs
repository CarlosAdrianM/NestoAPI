using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Rapports
{
    /// <summary>NestoAPI#603: lo que el motor de sugerencias lee de la BD (todo de solo lectura).</summary>
    public interface IRepositorioCarteraContacto
    {
        /// <summary>
        /// Clientes activos del vendedor (por la ficha o, NestoAPI#620, por grupo de producto) con alguna compra en 24 meses
        /// (de su grupo, si lo lleva por grupo), con sus pedidos, importe y últimos contactos (sin los rapports del otro vendedor del cliente).
        /// </summary>
        Task<List<ClienteCarteraContacto>> LeerCartera(string vendedor, DateTime hoy);
        /// <summary>
        /// Sugerencia 782: los clientes (número + contacto) con los que habló el vendedor cada día (rapports suyos Estado 0 de
        /// tipo T/V/W), de <see cref="RepositorioCarteraContactoSql.DIAS_CONTACTOS"/> días atrás a hoy, una fila por cliente
        /// y día. Los cuenta <see cref="MotorSugerenciasContacto.ContarContactos"/> cruzando con la cartera.
        /// </summary>
        Task<List<ContactoClienteDia>> LeerContactos(string vendedor, DateTime hoy);
        /// <summary>Delegación (almacén) del vendedor para los festivos: DelegaciónDefecto de su usuario, o ALG.</summary>
        Task<string> LeerDelegacion(string vendedor);
    }

    /// <summary>
    /// NestoAPI#603: consultas de la cartera. Ligera a propósito (≈1 s para la cartera más grande, MPP, 600 clientes):
    /// se lanza en cada apertura para que quien acaba de ser contactado desaparezca de la lista. Lo pesado (el modelo,
    /// SQL_OBTENER_CLIENTES) va aparte y cacheado (<see cref="ProbabilidadesContactoModelo"/>).
    /// </summary>
    public class RepositorioCarteraContactoSql : IRepositorioCarteraContacto
    {
        internal const int TIMEOUT_SEGUNDOS = 60;

        /// <summary>
        /// Estados de cliente que nunca entran en la cartera de sugerencias (ni en la cartera ni en la inferencia del
        /// modelo, <see cref="RepositorioFeaturesContactoSql"/>): 7 = <see cref="Constantes.Clientes.Estados.COMISIONA_SIN_VISITA"/>,
        /// 67 y 95 = <see cref="Constantes.Clientes.Estados.UNA_SOLA_COMPRA_AMAZON"/>. Única fuente: las dos consultas lo concatenan.
        /// </summary>
        internal const string ESTADOS_EXCLUIDOS_SQL = "7, 67, 95";

        /// <summary>
        /// NestoAPI#620 (Carlos, 20/10/26): «sin acción comercial» (<see cref="Constantes.Clientes.Estados.SIN_ACCION_COMERCIAL_SOLO_PELUQUERIA"/> = 11,
        /// <see cref="Constantes.Clientes.Estados.SIN_ACCION_COMERCIAL_ESTETICA_Y_PELUQUERIA"/> = 22 y
        /// <see cref="Constantes.Clientes.Estados.SIN_ACCION_COMERCIAL_SOLO_ESTETICA"/> = 93): fuera de las dos listas. Cada estado
        /// vale para su asignación: el de la ficha (Clientes.Estado) quita al cliente de la lista del vendedor de la ficha y
        /// el de la fila de grupo, de la del vendedor de ese grupo (una ficha en 11 «solo peluquería» sigue en la lista del
        /// de peluquería si su fila de grupo está activa).
        /// </summary>
        internal const string ESTADOS_SIN_ACCION_COMERCIAL_SQL = "11, 22, 93";

        /// <summary>
        /// NestoAPI#620: a quién lleva el vendedor. Única fuente para la cartera (<see cref="SQL_CARTERA"/>) y para la
        /// inferencia del modelo (<see cref="RepositorioFeaturesContactoSql"/>).
        /// <list type="bullet">
        /// <item><b>#Asignacion</b> (Cliente, Contacto, TodosLosGrupos): los de la ficha (Clientes.Vendedor, TodosLosGrupos = 1)
        /// más los que lleva por grupo de producto (VendedoresClienteGrupoProducto, el de peluquería; TodosLosGrupos = 0), con
        /// el Estado de la fila de grupo con el mismo filtro que la ficha (≥ 0 y fuera <see cref="ESTADOS_EXCLUIDOS_SQL"/>).
        /// Si está en las dos, una sola fila (cliente + contacto) y manda la ficha. El filtro de estado de la FICHA lo aplica
        /// quien la usa (también a los que vienen por grupo: un cliente anulado no es cartera de nadie).</item>
        /// <item><b>#GruposCartera</b> (Cliente, Contacto, Grupo): los grupos por los que lleva a los que NO son suyos por la
        /// ficha. Para ellos solo cuentan las compras de esos grupos (las de peluquería para el de peluquería).</item>
        /// <item><b>#GruposAjenos</b> (Cliente, Contacto, Grupo): para los suyos por la ficha, los grupos que lleva OTRO vendedor
        /// (no el general) en VendedoresClienteGrupoProducto, sea cual sea el estado de esa fila. Esas compras no le cuentan
        /// (simetría, Carlos 20/10/26): cada vendedor, solo sus compras. Sin vendedor de grupo, le cuentan todas.</item>
        /// <item>Sin acción comercial (<see cref="ESTADOS_SIN_ACCION_COMERCIAL_SQL"/>): fuera, cada estado en su asignación.</item>
        /// </list>
        /// </summary>
        internal const string SQL_ASIGNACION = @"
            SELECT a.Cliente, a.Contacto, MAX(a.TodosLosGrupos) TodosLosGrupos
            INTO #Asignacion
            FROM (
                SELECT c.[Nº Cliente] Cliente, c.Contacto, 1 TodosLosGrupos
                FROM Clientes c WITH (NOLOCK)
                WHERE c.Empresa = '1' AND c.Vendedor = @Vendedor AND c.Estado NOT IN (" + ESTADOS_SIN_ACCION_COMERCIAL_SQL + @")
                UNION ALL
                SELECT v.Cliente, v.Contacto, 0 TodosLosGrupos
                FROM VendedoresClienteGrupoProducto v WITH (NOLOCK)
                WHERE v.Empresa = '1' AND v.Vendedor = @Vendedor AND v.Estado >= 0 AND v.Estado NOT IN (" + ESTADOS_EXCLUIDOS_SQL + @")
                    AND v.Estado NOT IN (" + ESTADOS_SIN_ACCION_COMERCIAL_SQL + @")
            ) a
            GROUP BY a.Cliente, a.Contacto;
            CREATE CLUSTERED INDEX IX_Asignacion ON #Asignacion (Cliente, Contacto);

            SELECT DISTINCT v.Cliente, v.Contacto, v.GrupoProducto Grupo
            INTO #GruposCartera
            FROM VendedoresClienteGrupoProducto v WITH (NOLOCK)
            INNER JOIN #Asignacion a ON a.Cliente = v.Cliente AND a.Contacto = v.Contacto AND a.TodosLosGrupos = 0
            WHERE v.Empresa = '1' AND v.Vendedor = @Vendedor AND v.Estado >= 0 AND v.Estado NOT IN (" + ESTADOS_EXCLUIDOS_SQL + @")
                AND v.Estado NOT IN (" + ESTADOS_SIN_ACCION_COMERCIAL_SQL + @");

            SELECT DISTINCT v.Cliente, v.Contacto, v.GrupoProducto Grupo
            INTO #GruposAjenos
            FROM VendedoresClienteGrupoProducto v WITH (NOLOCK)
            INNER JOIN #Asignacion a ON a.Cliente = v.Cliente AND a.Contacto = v.Contacto AND a.TodosLosGrupos = 1
            WHERE v.Empresa = '1' AND v.Vendedor <> @Vendedor AND v.Vendedor <> '" + Constantes.Vendedores.VENDEDOR_GENERAL + @"';";

        /// <summary>
        /// NestoAPI#620: la línea cuenta para el vendedor si el cliente es suyo por la ficha y ese grupo no lo lleva otro
        /// vendedor, o si es de uno de los grupos por los que lo lleva (x = #Cartera, l = LinPedidoVta).
        /// </summary>
        internal const string FILTRO_GRUPO_LINEA = @"((x.TodosLosGrupos = 1 AND NOT EXISTS (SELECT 1 FROM #GruposAjenos j
                    WHERE j.Cliente = x.Cliente AND j.Contacto = x.Contacto AND j.Grupo = l.Grupo))
                OR EXISTS (SELECT 1 FROM #GruposCartera g
                    WHERE g.Cliente = x.Cliente AND g.Contacto = x.Contacto AND g.Grupo = l.Grupo))";

        internal const string SQL_CARTERA = @"
            SET NOCOUNT ON;
            DECLARE @Hace12Meses date = DATEADD(month, -12, @Hoy);
            DECLARE @Hace24Meses date = DATEADD(month, -@MesesCartera, @Hoy);
            DECLARE @Hace60Dias date = DATEADD(day, -60, @Hoy);
" + SQL_ASIGNACION + @"

            SELECT c.[Nº Cliente] Cliente, c.Contacto, c.Nombre, c.Dirección Direccion, c.CodPostal, c.Población Poblacion,
                c.Provincia, c.Teléfono Telefono, a.TodosLosGrupos
            INTO #Cartera
            FROM Clientes c WITH (NOLOCK)
            INNER JOIN #Asignacion a ON a.Cliente = c.[Nº Cliente] AND a.Contacto = c.Contacto
            WHERE c.Empresa = '1' AND c.Estado >= 0 AND c.Estado NOT IN (" + ESTADOS_EXCLUIDOS_SQL + @");
            CREATE CLUSTERED INDEX IX_Cartera ON #Cartera (Cliente, Contacto);

            -- NestoAPI#620: los OTROS vendedores que llevan hoy al cliente (el de la ficha o el de un grupo, sin el general):
            -- sus rapports no cuentan como contacto de este vendedor (cada uno, su cadencia). Los de cualquier otro (sin
            -- vendedor, el general, o alguien que ya no lo lleva) sí cuentan, como antes.
            SELECT DISTINCT o.Cliente, o.Contacto, o.Vendedor
            INTO #OtrosVendedores
            FROM (
                SELECT c.[Nº Cliente] Cliente, c.Contacto, c.Vendedor
                FROM Clientes c WITH (NOLOCK)
                INNER JOIN #Cartera x ON x.Cliente = c.[Nº Cliente] AND x.Contacto = c.Contacto
                WHERE c.Empresa = '1'
                UNION ALL
                SELECT v.Cliente, v.Contacto, v.Vendedor
                FROM VendedoresClienteGrupoProducto v WITH (NOLOCK)
                INNER JOIN #Cartera x ON x.Cliente = v.Cliente AND x.Contacto = v.Contacto
                WHERE v.Empresa = '1'
            ) o
            WHERE o.Vendedor IS NOT NULL AND o.Vendedor <> @Vendedor AND o.Vendedor <> '" + Constantes.Vendedores.VENDEDOR_GENERAL + @"';

            -- Compras facturadas: un pedido = un Número de pedido con alguna línea facturada, con la fecha del pedido
            -- (CabPedidoVta.Fecha), como #EnCurso. Antes era un día distinto con albarán (no la fecha de factura, que en
            -- los FDM agrupa todo el mes en un día), pero un pedido servido en dos entregas contaba dos veces y el
            -- «último pedido» salía con la fecha del último albarán (31931: 22 días de albarán por 20 pedidos; el
            -- albarán del 28/09 era del pedido del 08/09 y el último pedido era del 17/09). La ventana va por la fecha
            -- del pedido; [Fecha Factura] >= @Hace24Meses queda solo como prefiltro (se factura después de pedir).
            SELECT l.[Nº Cliente] Cliente, l.Contacto, l.Número Pedido, CAST(cab.Fecha AS date) Dia,
                SUM(l.[Base Imponible]) Importe
            INTO #Compras
            FROM LinPedidoVta l WITH (NOLOCK)
            INNER JOIN CabPedidoVta cab WITH (NOLOCK) ON cab.Empresa = l.Empresa AND cab.Número = l.Número
            INNER JOIN #Cartera x ON x.Cliente = l.[Nº Cliente] AND x.Contacto = l.Contacto
            WHERE l.Empresa = '1' AND l.Estado = 4 AND l.[Base Imponible] > 0 AND l.SubGrupo <> 'MMP'
                AND l.[Fecha Factura] >= @Hace24Meses AND cab.Fecha >= @Hace24Meses
                AND " + FILTRO_GRUPO_LINEA + @"
            GROUP BY l.[Nº Cliente], l.Contacto, l.Número, CAST(cab.Fecha AS date);

            -- Pedidos todavía sin facturar (pendiente, en curso, albarán): solo cuentan para «pidió hace poco», con la
            -- misma fecha del pedido que #Compras.
            SELECT l.[Nº Cliente] Cliente, l.Contacto, MAX(CAST(cab.Fecha AS date)) Dia
            INTO #EnCurso
            FROM LinPedidoVta l WITH (NOLOCK)
            INNER JOIN CabPedidoVta cab WITH (NOLOCK) ON cab.Empresa = l.Empresa AND cab.Número = l.Número
            INNER JOIN #Cartera x ON x.Cliente = l.[Nº Cliente] AND x.Contacto = l.Contacto
            WHERE l.Empresa = '1' AND l.Estado BETWEEN -1 AND 2 AND l.[Base Imponible] > 0 AND l.SubGrupo <> 'MMP'
                AND cab.Fecha >= @Hace60Dias
                AND " + FILTRO_GRUPO_LINEA + @"
            GROUP BY l.[Nº Cliente], l.Contacto;

            -- NestoAPI#620 (Carlos, 20/10/26): CUALQUIER pedido reciente del cliente (de cualquier grupo, facturado o no)
            -- cuenta como «pidió hace poco» para todos sus vendedores. La cadencia sigue con las compras de cada uno.
            SELECT l.[Nº Cliente] Cliente, l.Contacto, MAX(CAST(cab.Fecha AS date)) Dia
            INTO #PedidoReciente
            FROM LinPedidoVta l WITH (NOLOCK)
            INNER JOIN CabPedidoVta cab WITH (NOLOCK) ON cab.Empresa = l.Empresa AND cab.Número = l.Número
            INNER JOIN #Cartera x ON x.Cliente = l.[Nº Cliente] AND x.Contacto = l.Contacto
            WHERE l.Empresa = '1' AND (l.Estado BETWEEN -1 AND 2 OR l.Estado = 4) AND l.[Base Imponible] > 0 AND l.SubGrupo <> 'MMP'
                AND cab.Fecha >= @Hace60Dias
            GROUP BY l.[Nº Cliente], l.Contacto;

            SELECT s.Número Cliente, s.Contacto,
                MAX(CASE WHEN s.Estado = 0 THEN s.Fecha END) UltimoContacto,
                MAX(CASE WHEN s.Estado = 1 THEN s.Fecha END) UltimoIntento
            INTO #Rapports
            FROM SeguimientoCliente s WITH (NOLOCK)
            INNER JOIN #Cartera x ON x.Cliente = s.Número AND x.Contacto = s.Contacto
            WHERE s.Fecha >= @Hace24Meses AND s.Estado IN (0, 1) AND s.Tipo IN ('T', 'V', 'W')
                AND NOT EXISTS (SELECT 1 FROM #OtrosVendedores o
                    WHERE o.Cliente = s.Número AND o.Contacto = s.Contacto AND o.Vendedor = s.Vendedor)
            GROUP BY s.Número, s.Contacto;

            SELECT x.Cliente, x.Contacto, x.Nombre, x.Direccion, x.CodPostal, x.Poblacion, x.Provincia, x.Telefono,
                SUM(CASE WHEN cp.Dia >= @Hace12Meses THEN 1 ELSE 0 END) Pedidos12Meses,
                SUM(CASE WHEN cp.Dia >= @Hace12Meses THEN cp.Importe ELSE 0 END) Importe12Meses,
                COUNT(cp.Pedido) Pedidos24Meses,
                MAX(cp.Dia) UltimaCompra,
                MAX(ec.Dia) UltimoEnCurso,
                MAX(pr.Dia) UltimoPedidoCliente,
                MAX(r.UltimoContacto) UltimoContacto,
                MAX(r.UltimoIntento) UltimoIntento
            FROM #Cartera x
            INNER JOIN #Compras cp ON cp.Cliente = x.Cliente AND cp.Contacto = x.Contacto
            LEFT JOIN #EnCurso ec ON ec.Cliente = x.Cliente AND ec.Contacto = x.Contacto
            LEFT JOIN #PedidoReciente pr ON pr.Cliente = x.Cliente AND pr.Contacto = x.Contacto
            LEFT JOIN #Rapports r ON r.Cliente = x.Cliente AND r.Contacto = x.Contacto
            GROUP BY x.Cliente, x.Contacto, x.Nombre, x.Direccion, x.CodPostal, x.Poblacion, x.Provincia, x.Telefono;";

        /// <summary>
        /// Sugerencia 782: días hacia atrás de <see cref="SQL_CONTACTOS"/>. Cubre el mes y la semana, y los 62 días de la racha de
        /// las frases (<see cref="GeneradorFrasesRitmo.CalcularSituacion"/>), que antes leía su propia consulta de rapports por día.
        /// </summary>
        internal const int DIAS_CONTACTOS = 62;

        /// <summary>
        /// Sugerencia 782: una fila por cliente + contacto + día (no un recuento de rapports). Los contadores de clientes
        /// distintos de la cartera los saca <see cref="MotorSugerenciasContacto.ContarContactos"/> en C# con la cartera ya
        /// leída: así no se repite aquí la asignación de <see cref="SQL_CARTERA"/>. Mismo filtro que antes (los rapports del
        /// vendedor por Vendedor y Fecha); a lo sumo unos cientos de filas.
        /// </summary>
        internal const string SQL_CONTACTOS = @"
            SET NOCOUNT ON;
            SELECT DISTINCT s.Número Cliente, s.Contacto, CAST(s.Fecha AS date) Dia
            FROM SeguimientoCliente s WITH (NOLOCK)
            WHERE s.Vendedor = @Vendedor AND s.Fecha >= @Desde AND s.Fecha < @Manana AND s.Estado = 0 AND s.Tipo IN ('T', 'V', 'W');";

        internal const string SQL_DELEGACION = @"
            SELECT TOP 1 RTRIM(p.Valor)
            FROM UsuarioVendedor uv WITH (NOLOCK)
            INNER JOIN ParametrosUsuario p WITH (NOLOCK)
                ON p.Empresa = '1' AND p.Clave = N'DelegaciónDefecto'
                AND RTRIM(p.Usuario) = RTRIM(SUBSTRING(uv.Usuario, CHARINDEX('\', uv.Usuario) + 1, 100))
            WHERE uv.Vendedor = @Vendedor AND RTRIM(ISNULL(p.Valor, '')) <> '';";

        public static DateTime InicioSemana(DateTime hoy) => hoy.Date.AddDays(-(((int)hoy.DayOfWeek + 6) % 7));
        public static DateTime InicioMes(DateTime hoy) => new DateTime(hoy.Year, hoy.Month, 1);

        public Task<List<ClienteCarteraContacto>> LeerCartera(string vendedor, DateTime hoy)
        {
            return Task.Run(() => ReintentosSql.ReintentarSiDeadlock(() =>
            {
                var cartera = new List<ClienteCarteraContacto>();
                using (SqlConnection conexion = AbrirConexion())
                using (var comando = new SqlCommand(SQL_CARTERA, conexion) { CommandTimeout = TIMEOUT_SEGUNDOS })
                {
                    _ = comando.Parameters.Add(new SqlParameter("@Vendedor", SqlDbType.VarChar, 10) { Value = vendedor.Trim() });
                    _ = comando.Parameters.Add(new SqlParameter("@Hoy", SqlDbType.Date) { Value = hoy.Date });
                    _ = comando.Parameters.Add(new SqlParameter("@MesesCartera", SqlDbType.Int) { Value = UmbralesSugerenciasContacto.MESES_CARTERA });
                    using (SqlDataReader lector = comando.ExecuteReader())
                    {
                        while (lector.Read())
                        {
                            DateTime? ultimaCompra = Fecha(lector["UltimaCompra"]);
                            DateTime? enCurso = Fecha(lector["UltimoEnCurso"]);
                            cartera.Add(new ClienteCarteraContacto
                            {
                                Cliente = Texto(lector["Cliente"]),
                                Contacto = Texto(lector["Contacto"]),
                                Nombre = Texto(lector["Nombre"]),
                                Direccion = Texto(lector["Direccion"]),
                                CodigoPostal = Texto(lector["CodPostal"]),
                                Poblacion = Texto(lector["Poblacion"]),
                                Provincia = Texto(lector["Provincia"]),
                                Telefono = Texto(lector["Telefono"]),
                                Pedidos12Meses = Convert.ToInt32(lector["Pedidos12Meses"]),
                                Importe12Meses = Convert.ToDecimal(lector["Importe12Meses"]),
                                Pedidos24Meses = Convert.ToInt32(lector["Pedidos24Meses"]),
                                UltimoPedido = MasReciente(ultimaCompra, enCurso),
                                UltimoPedidoCliente = Fecha(lector["UltimoPedidoCliente"]),
                                UltimoContacto = Fecha(lector["UltimoContacto"]),
                                UltimoIntento = Fecha(lector["UltimoIntento"])
                            });
                        }
                    }
                }
                return cartera;
            }));
        }

        public Task<List<ContactoClienteDia>> LeerContactos(string vendedor, DateTime hoy)
        {
            return Task.Run(() => ReintentosSql.ReintentarSiDeadlock(() =>
            {
                var contactos = new List<ContactoClienteDia>();
                using (SqlConnection conexion = AbrirConexion())
                using (var comando = new SqlCommand(SQL_CONTACTOS, conexion) { CommandTimeout = TIMEOUT_SEGUNDOS })
                {
                    _ = comando.Parameters.Add(new SqlParameter("@Vendedor", SqlDbType.VarChar, 10) { Value = vendedor.Trim() });
                    _ = comando.Parameters.Add(new SqlParameter("@Desde", SqlDbType.DateTime) { Value = hoy.Date.AddDays(-DIAS_CONTACTOS) });
                    _ = comando.Parameters.Add(new SqlParameter("@Manana", SqlDbType.DateTime) { Value = hoy.Date.AddDays(1) });
                    using (SqlDataReader lector = comando.ExecuteReader())
                    {
                        while (lector.Read())
                        {
                            contactos.Add(new ContactoClienteDia
                            {
                                Cliente = Texto(lector["Cliente"]),
                                Contacto = Texto(lector["Contacto"]),
                                Dia = Convert.ToDateTime(lector["Dia"]).Date
                            });
                        }
                    }
                }
                return contactos;
            }));
        }

        public Task<string> LeerDelegacion(string vendedor)
        {
            return Task.Run(() =>
            {
                using (SqlConnection conexion = AbrirConexion())
                using (var comando = new SqlCommand(SQL_DELEGACION, conexion) { CommandTimeout = TIMEOUT_SEGUNDOS })
                {
                    _ = comando.Parameters.Add(new SqlParameter("@Vendedor", SqlDbType.VarChar, 10) { Value = vendedor.Trim() });
                    object valor = comando.ExecuteScalar();
                    string delegacion = valor == null || valor == DBNull.Value ? null : valor.ToString().Trim();
                    return string.IsNullOrEmpty(delegacion) ? Constantes.Almacenes.ALGETE : delegacion;
                }
            });
        }

        private static SqlConnection AbrirConexion()
        {
            string cadena;
            using (var contexto = new NVEntities())
            {
                cadena = contexto.Database.Connection.ConnectionString;
            }
            var conexion = new SqlConnection(cadena);
            conexion.Open();
            return conexion;
        }

        private static string Texto(object valor) => valor == DBNull.Value ? null : valor.ToString().Trim();
        private static DateTime? Fecha(object valor) => valor == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(valor);
        internal static DateTime? MasReciente(DateTime? a, DateTime? b) => !a.HasValue ? b : !b.HasValue ? a : (a.Value >= b.Value ? a : b);
    }
}
