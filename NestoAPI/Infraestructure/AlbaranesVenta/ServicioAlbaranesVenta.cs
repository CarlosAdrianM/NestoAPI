using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Data.SqlClient;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.AlbaranesVenta
{
    public class ServicioAlbaranesVenta : IServicioAlbaranesVenta
    {
        private readonly NVEntities db;
        private readonly bool dbEsExterno;
        private readonly Func<string, int, int, string, Task> trasAlbaran;

        /// <summary>
        /// Constructor por defecto. Crea su propio NVEntities interno.
        /// </summary>
        public ServicioAlbaranesVenta() : this(null)
        {
        }

        /// <summary>
        /// Constructor que permite inyectar un NVEntities externo.
        /// Esto es necesario para evitar conflictos de concurrencia cuando se usa
        /// desde GestorFacturacionRutas, que ya tiene su propio contexto.
        /// </summary>
        /// <param name="dbExterno">NVEntities externo. Si es null, se crea uno interno.</param>
        public ServicioAlbaranesVenta(NVEntities dbExterno) : this(dbExterno, null)
        {
        }

        /// <summary>
        /// NestoAPI#542: lo que se hace justo después de cada albarán (la nota de entrega automática con lo
        /// pendiente). Sustituible en tests; en producción (null) es CreadorNotaEntregaPendiente.TrasAlbaran,
        /// que va con su propio contexto y nunca lanza.
        /// </summary>
        internal ServicioAlbaranesVenta(NVEntities dbExterno, Func<string, int, int, string, Task> trasAlbaran)
        {
            this.trasAlbaran = trasAlbaran ?? NotasEntrega.CreadorNotaEntregaPendiente.TrasAlbaran;
            if (dbExterno != null)
            {
                db = dbExterno;
                dbEsExterno = true;
            }
            else
            {
                db = new NVEntities();
                dbEsExterno = false;
            }
        }

        /// <summary>
        /// 28/09/26 (pedidos 926037, 926373, 927014, 927146): prdCrearAlbaránVta, regla del 07/04/22, devuelve a
        /// pendiente con Recoger = 0 las líneas de base 0 (regalos, expositores) marcadas para recoger cuando son lo
        /// único «en carpeta» del pedido. Si esas líneas ya tienen picking, trgLinPedidoVtaUpd prohíbe cambiar Recoger
        /// y el albarán falla («No puede cambiar el campo recoger si la linea tiene picking»). Aquí se hace lo mismo que
        /// el SP ANTES de llamarlo, quitando también el picking de esas líneas en el mismo UPDATE (el trigger lo admite
        /// porque la línea deja de tener picking). Solo las que van ENTERAS a recoger: no llevan nada en esta entrega.
        /// Así el SP ya no encuentra nada que cambiar. Misma condición de pedido que el SP.
        /// </summary>
        internal static List<int> RegalosEnCarpetaConPicking(IEnumerable<LinPedidoVta> lineasPedido)
        {
            List<LinPedidoVta> lineas = (lineasPedido ?? Enumerable.Empty<LinPedidoVta>()).ToList();
            bool aplicaLaRegla =
                lineas.Any(l => l.Recoger != 0 && l.Base_Imponible == 0 && l.Estado == Constantes.EstadosLineaVenta.EN_CURSO)
                && !lineas.Any(l => l.Recoger != 0 && l.Base_Imponible != 0)
                && lineas.Any(l => l.Base_Imponible != 0);
            if (!aplicaLaRegla)
            {
                return new List<int>();
            }
            return lineas
                .Where(l => l.Recoger != 0 && l.Base_Imponible == 0 && l.Estado == Constantes.EstadosLineaVenta.EN_CURSO
                    && l.Picking > 0 && l.Recoger == l.Cantidad)
                .Select(l => l.Nº_Orden)
                .ToList();
        }

        private async Task SacarDeCarpetaLosRegalosConPicking(string empresa, int pedido)
        {
            List<LinPedidoVta> lineas = db.LinPedidoVtas.AsNoTracking()
                .Where(l => l.Empresa == empresa && l.Número == pedido)
                .ToList();
            List<int> ids = RegalosEnCarpetaConPicking(lineas);
            if (!ids.Any())
            {
                return;
            }
            // SQL directo y solo sobre esas líneas: el contexto puede ser el de la facturación de rutas y un
            // SaveChanges aquí guardaría también lo que ese proceso tenga a medias.
            string lista = string.Join(",", ids);
            _ = await db.Database.ExecuteSqlCommandAsync(
                "UPDATE LinPedidoVta SET Estado = -1, Recoger = 0, Picking = 0 WHERE [Nº Orden] IN (" + lista + ") " +
                "AND Estado = 1 AND Recoger <> 0 AND Recoger = Cantidad AND [Base Imponible] = 0 AND Picking > 0").ConfigureAwait(false);
        }

        public async Task<int> CrearAlbaran(string empresa, int pedido, string usuario, DateTime? fechaEntrega = null)
        {
            // Usar el db de la clase (puede ser externo o interno según el constructor)
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
                Value = fechaEntrega ?? DateTime.Now // sin indicar, la de hoy (histórico de rutas)
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

            int resultadoProcedimiento;
            try
            {
                await SacarDeCarpetaLosRegalosConPicking(empresa, pedido).ConfigureAwait(false);

                // Ejecutar el procedimiento almacenado y capturar el valor de retorno
                var resultadoDirecto = await db.Database.ExecuteSqlCommandAsync("EXEC @Resultado = prdCrearAlbaránVta @Empresa, @Pedido, @FechaEntrega, @ImporteMinimo, @Usuario", resultadoParametro, empresaParam, pedidoParam, fechaEntregaParam, importeMinimoParam, usuarioParam);
                // Obtener el valor de retorno del parámetro
                resultadoProcedimiento = (int)resultadoParametro.Value;
            }
            catch (Exception ex)
            {
                throw new Exception("Error al crear el albarán", ex);
            }
            // NestoAPI#542: con el albarán ya hecho (el SP devuelve -1/-2 si falla), lo pendiente de las líneas
            // con Recoger pasa a su nota de entrega. Fuera del try: el gancho no lanza nunca.
            if (resultadoProcedimiento > 0)
            {
                await trasAlbaran(empresa, pedido, resultadoProcedimiento, usuario).ConfigureAwait(false);
            }
            return resultadoProcedimiento;
        }
    }
}