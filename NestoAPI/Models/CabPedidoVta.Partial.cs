namespace NestoAPI.Models
{
    /// <summary>
    /// Clase parcial de CabPedidoVta para métodos de clonación
    /// </summary>
    public partial class CabPedidoVta
    {
        /// <summary>
        /// Clona el pedido cambiando la empresa destino.
        /// Este método copia TODAS las propiedades excepto LinPedidoVtas (debe hacerse manualmente).
        /// VENTAJA: Si se agregan nuevas propiedades al modelo, se copiarán automáticamente.
        /// </summary>
        /// <param name="empresaDestino">Empresa destino</param>
        /// <returns>Nuevo pedido clonado con la empresa destino</returns>
        public CabPedidoVta ClonarParaEmpresa(string empresaDestino)
        {
            return new CabPedidoVta
            {
                // Clave primaria - cambiamos la empresa
                Empresa = empresaDestino,
                Número = this.Número,

                // Todas las demás propiedades (excepto navegación)
                Nº_Cliente = this.Nº_Cliente,
                Contacto = this.Contacto,
                Fecha = this.Fecha,
                Forma_Pago = this.Forma_Pago,
                PlazosPago = this.PlazosPago,
                Primer_Vencimiento = this.Primer_Vencimiento,
                IVA = this.IVA,
                Vendedor = this.Vendedor,
                Comentarios = this.Comentarios,
                ComentarioPicking = this.ComentarioPicking,
                Periodo_Facturacion = this.Periodo_Facturacion,
                Ruta = this.Ruta,
                Serie = this.Serie,
                CCC = this.CCC,
                Origen = this.Origen,
                Agrupada = this.Agrupada,
                MotivoDevolución = this.MotivoDevolución,
                ContactoCobro = this.ContactoCobro,
                NoComisiona = this.NoComisiona,
                NotaEntrega = this.NotaEntrega,
                vtoBuenoPlazosPago = this.vtoBuenoPlazosPago,
                Operador = this.Operador,
                FijarPrimerVto = this.FijarPrimerVto,
                MantenerJunto = this.MantenerJunto,
                ServirJunto = this.ServirJunto,
                Usuario = this.Usuario,
                Fecha_Modificación = this.Fecha_Modificación
                // NO copiamos: LinPedidoVtas, Cliente, VendedoresPedidoGrupoProductoes,
                // EnviosAgencias, Prepagos, EfectosPedidoVentas (propiedades de navegación)
                // NO copiamos: RowVersion (debe ser nuevo)
            };
        }
    }
}
