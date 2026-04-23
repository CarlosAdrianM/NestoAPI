namespace NestoAPI.Models.PedidosVenta.ServirJunto
{
    public class ProductoBonificadoConCantidadRequest
    {
        public string ProductoId { get; set; }
        public int Cantidad { get; set; }

        // NestoAPI#175: identifica líneas bonificadas de Ganavisiones dentro de LineasPedido
        // para que ValidadorDisponibilidadRegalos pueda bloquear el desmarcado de servirJunto
        // desde DetallePedido (donde ProductosBonificadosConCantidad viene vacío).
        // Se ignora en ProductosBonificadosConCantidad (esas líneas son bonificados por definición).
        public bool EsBonificadoGanavisiones { get; set; }
    }
}
