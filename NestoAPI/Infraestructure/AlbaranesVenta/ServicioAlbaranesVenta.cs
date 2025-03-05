using NestoAPI.Models;
using System;
using System.Data;
using System.Data.SqlClient;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.AlbaranesVenta
{
    public class ServicioAlbaranesVenta : IServicioAlbaranesVenta
    {
        public async Task<int> CrearAlbaran(string empresa, int pedido, string usuario)
        {            
            using (NVEntities db = new NVEntities())
            {
                SqlParameter empresaParam = new SqlParameter("@Empresa", System.Data.SqlDbType.Char)
                {
                    Value = empresa
                };
                SqlParameter pedidoParam = new SqlParameter("@Pedido", System.Data.SqlDbType.Int)
                {
                    Value = pedido
                };
                SqlParameter fechaEntregaParam = new SqlParameter("@FechaEntrega", System.Data.SqlDbType.DateTime)
                {
                    Value = DateTime.Now // De momento dejamos siempre la de hoy
                };
                SqlParameter importeMinimoParam = new SqlParameter("@ImporteMinimo", System.Data.SqlDbType.Decimal)
                {
                    Value = 0 // De momento ponemos siempre que sea cero
                };
                SqlParameter usuarioParam = new SqlParameter("@Usuario", System.Data.SqlDbType.Char)
                {
                    Value = usuario
                };
                var resultadoParametro = new SqlParameter
                {
                    ParameterName = "@Resultado",
                    SqlDbType = SqlDbType.Int,
                    Direction = ParameterDirection.Output // Configurar para capturar el valor de retorno
                };
                
                try
                {
                    // Ejecutar el procedimiento almacenado y capturar el valor de retorno
                    var resultadoDirecto = await db.Database.ExecuteSqlCommandAsync("EXEC @Resultado = prdCrearAlbaránVta @Empresa, @Pedido, @FechaEntrega, @ImporteMinimo, @Usuario", resultadoParametro, empresaParam, pedidoParam, fechaEntregaParam, importeMinimoParam, usuarioParam);
                    // Obtener el valor de retorno del parámetro
                    var resultadoProcedimiento = (int)resultadoParametro.Value;
                    return resultadoProcedimiento;
                }
                catch (Exception ex)
                {
                    throw new Exception("Error al crear el albarán", ex);
                }
            };
        }
    }
}