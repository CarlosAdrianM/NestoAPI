using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.AlbaranesVenta
{
    public class ServicioAlbaranesVenta : IServicioAlbaranesVenta
    {
        private readonly NVEntities db;
        private readonly bool dbEsExterno;
        private readonly Func<string, int, int, string, Task> trasAlbaran;
        private readonly Func<string, int, Task<List<FaltaSinQuitar>>> faltasSinQuitar;

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
            : this(dbExterno, trasAlbaran, null)
        {
        }

        /// <summary>
        /// Nesto#508: <paramref name="faltasSinQuitar"/> dice lo que el mozo dio por «No está» en Ariadna y sigue en el
        /// pedido. Sustituible en tests; en producción (null) es FaltasSinQuitarSalida con el mismo contexto.
        /// </summary>
        internal ServicioAlbaranesVenta(NVEntities dbExterno, Func<string, int, int, string, Task> trasAlbaran,
            Func<string, int, Task<List<FaltaSinQuitar>>> faltasSinQuitar)
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
            this.faltasSinQuitar = faltasSinQuitar ?? new FaltasSinQuitarSalida(db).Leer;
        }

        public async Task<int> CrearAlbaran(string empresa, int pedido, string usuario, DateTime? fechaEntrega = null)
        {
            // Nesto#508: este es el núcleo común de todos los albaranes (Facturar rutas, «Facturar al imprimir
            // etiqueta», los botones del detalle): ninguno sale con productos que el mozo no ha encontrado.
            List<FaltaSinQuitar> faltas = await faltasSinQuitar(empresa, pedido).ConfigureAwait(false);
            if (faltas != null && faltas.Count > 0)
            {
                throw new NestoBusinessException(FaltasSinQuitarSalida.Mensaje(pedido, faltas))
                {
                    StatusCode = System.Net.HttpStatusCode.Conflict
                };
            }

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