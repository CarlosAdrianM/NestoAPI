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
        /// <summary>Clientes activos del vendedor con alguna compra en 24 meses, con sus pedidos, importe y últimos contactos.</summary>
        Task<List<ClienteCarteraContacto>> LeerCartera(string vendedor, DateTime hoy);
        /// <summary>Rapports Estado 0 de tipo T/V/W del vendedor hoy, desde el lunes y desde el día 1.</summary>
        Task<ContactosVendedor> LeerContactos(string vendedor, DateTime hoy);
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

        internal const string SQL_CARTERA = @"
            SET NOCOUNT ON;
            DECLARE @Hace12Meses date = DATEADD(month, -12, @Hoy);
            DECLARE @Hace24Meses date = DATEADD(month, -@MesesCartera, @Hoy);
            DECLARE @Hace60Dias date = DATEADD(day, -60, @Hoy);

            SELECT c.[Nº Cliente] Cliente, c.Contacto, c.Nombre, c.Dirección Direccion, c.CodPostal, c.Población Poblacion,
                c.Provincia, c.Teléfono Telefono
            INTO #Cartera
            FROM Clientes c WITH (NOLOCK)
            WHERE c.Empresa = '1' AND c.Estado >= 0 AND c.Estado NOT IN (7, 67) AND c.Vendedor = @Vendedor;
            CREATE CLUSTERED INDEX IX_Cartera ON #Cartera (Cliente, Contacto);

            -- Compras facturadas: un pedido = un día distinto con albarán (la fecha de factura agrupa el mes en los FDM).
            SELECT l.[Nº Cliente] Cliente, l.Contacto, CAST(ISNULL(l.[Fecha Albarán], l.[Fecha Factura]) AS date) Dia,
                SUM(l.[Base Imponible]) Importe
            INTO #Compras
            FROM LinPedidoVta l WITH (NOLOCK)
            INNER JOIN #Cartera x ON x.Cliente = l.[Nº Cliente] AND x.Contacto = l.Contacto
            WHERE l.Empresa = '1' AND l.Estado = 4 AND l.[Base Imponible] > 0 AND l.SubGrupo <> 'MMP'
                AND l.[Fecha Factura] >= @Hace24Meses
            GROUP BY l.[Nº Cliente], l.Contacto, CAST(ISNULL(l.[Fecha Albarán], l.[Fecha Factura]) AS date);

            -- Pedidos todavía sin facturar (pendiente, en curso, albarán): solo cuentan para «pidió hace poco».
            SELECT l.[Nº Cliente] Cliente, l.Contacto, MAX(CAST(cab.Fecha AS date)) Dia
            INTO #EnCurso
            FROM LinPedidoVta l WITH (NOLOCK)
            INNER JOIN CabPedidoVta cab WITH (NOLOCK) ON cab.Empresa = l.Empresa AND cab.Número = l.Número
            INNER JOIN #Cartera x ON x.Cliente = l.[Nº Cliente] AND x.Contacto = l.Contacto
            WHERE l.Empresa = '1' AND l.Estado BETWEEN -1 AND 2 AND l.[Base Imponible] > 0 AND l.SubGrupo <> 'MMP'
                AND cab.Fecha >= @Hace60Dias
            GROUP BY l.[Nº Cliente], l.Contacto;

            SELECT s.Número Cliente, s.Contacto,
                MAX(CASE WHEN s.Estado = 0 THEN s.Fecha END) UltimoContacto,
                MAX(CASE WHEN s.Estado = 1 THEN s.Fecha END) UltimoIntento
            INTO #Rapports
            FROM SeguimientoCliente s WITH (NOLOCK)
            INNER JOIN #Cartera x ON x.Cliente = s.Número AND x.Contacto = s.Contacto
            WHERE s.Fecha >= @Hace24Meses AND s.Estado IN (0, 1) AND s.Tipo IN ('T', 'V', 'W')
            GROUP BY s.Número, s.Contacto;

            SELECT x.Cliente, x.Contacto, x.Nombre, x.Direccion, x.CodPostal, x.Poblacion, x.Provincia, x.Telefono,
                SUM(CASE WHEN cp.Dia >= @Hace12Meses THEN 1 ELSE 0 END) Pedidos12Meses,
                SUM(CASE WHEN cp.Dia >= @Hace12Meses THEN cp.Importe ELSE 0 END) Importe12Meses,
                COUNT(cp.Dia) Pedidos24Meses,
                MAX(cp.Dia) UltimaCompra,
                MAX(ec.Dia) UltimoEnCurso,
                MAX(r.UltimoContacto) UltimoContacto,
                MAX(r.UltimoIntento) UltimoIntento
            FROM #Cartera x
            INNER JOIN #Compras cp ON cp.Cliente = x.Cliente AND cp.Contacto = x.Contacto
            LEFT JOIN #EnCurso ec ON ec.Cliente = x.Cliente AND ec.Contacto = x.Contacto
            LEFT JOIN #Rapports r ON r.Cliente = x.Cliente AND r.Contacto = x.Contacto
            GROUP BY x.Cliente, x.Contacto, x.Nombre, x.Direccion, x.CodPostal, x.Poblacion, x.Provincia, x.Telefono;";

        internal const string SQL_CONTACTOS = @"
            SET NOCOUNT ON;
            SELECT
                ISNULL(SUM(CASE WHEN s.Fecha >= @Hoy THEN 1 ELSE 0 END), 0) ContactosHoy,
                ISNULL(SUM(CASE WHEN s.Fecha >= @InicioSemana THEN 1 ELSE 0 END), 0) ContactosSemana,
                ISNULL(SUM(CASE WHEN s.Fecha >= @InicioMes THEN 1 ELSE 0 END), 0) ContactosMes
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
                                UltimoContacto = Fecha(lector["UltimoContacto"]),
                                UltimoIntento = Fecha(lector["UltimoIntento"])
                            });
                        }
                    }
                }
                return cartera;
            }));
        }

        public Task<ContactosVendedor> LeerContactos(string vendedor, DateTime hoy)
        {
            return Task.Run(() => ReintentosSql.ReintentarSiDeadlock(() =>
            {
                DateTime inicioSemana = InicioSemana(hoy);
                DateTime inicioMes = InicioMes(hoy);
                using (SqlConnection conexion = AbrirConexion())
                using (var comando = new SqlCommand(SQL_CONTACTOS, conexion) { CommandTimeout = TIMEOUT_SEGUNDOS })
                {
                    _ = comando.Parameters.Add(new SqlParameter("@Vendedor", SqlDbType.VarChar, 10) { Value = vendedor.Trim() });
                    _ = comando.Parameters.Add(new SqlParameter("@Hoy", SqlDbType.DateTime) { Value = hoy.Date });
                    _ = comando.Parameters.Add(new SqlParameter("@Manana", SqlDbType.DateTime) { Value = hoy.Date.AddDays(1) });
                    _ = comando.Parameters.Add(new SqlParameter("@InicioSemana", SqlDbType.DateTime) { Value = inicioSemana });
                    _ = comando.Parameters.Add(new SqlParameter("@InicioMes", SqlDbType.DateTime) { Value = inicioMes });
                    _ = comando.Parameters.Add(new SqlParameter("@Desde", SqlDbType.DateTime) { Value = inicioSemana < inicioMes ? inicioSemana : inicioMes });
                    using (SqlDataReader lector = comando.ExecuteReader())
                    {
                        return lector.Read()
                            ? new ContactosVendedor
                            {
                                Hoy = Convert.ToInt32(lector["ContactosHoy"]),
                                Semana = Convert.ToInt32(lector["ContactosSemana"]),
                                Mes = Convert.ToInt32(lector["ContactosMes"])
                            }
                            : new ContactosVendedor();
                    }
                }
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
