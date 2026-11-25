using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;

namespace NestoAPI.Infraestructure.ValidadoresServirJunto
{
    /// <summary>
    /// Incidencia 1460 (Sancho, 25/11/26): en una tienda el pedido solo puede salir «Según vaya entrando»
    /// (<see cref="ModosServicioPermitidos.MOTIVO_TIENDA"/>), así que el mensaje de los validadores de «Servir junto»
    /// no puede mandar a «elija otro modo de servicio»: ese modo nunca se acepta allí y el usuario se quedaba sin
    /// salida (tres usuarios, 13 veces en un día). Solo textos: las reglas no cambian.
    /// </summary>
    internal static class TextosServirJuntoTienda
    {
        /// <summary>¿Es una tienda? La regla única de <see cref="ModosServicioPermitidos.ClasificarAlmacen"/>.</summary>
        internal static bool EsTienda(string almacen)
            => ModosServicioPermitidos.ClasificarAlmacen(almacen) == ModosServicioPermitidos.TipoAlmacen.Tienda;

        /// <summary>«Alcobendas», «Reina»… (el código si no se conoce el nombre).</summary>
        internal static string Sede(string almacen) => CalculadoraFechaEntregaAgencia.NombreAlmacen(almacen);

        /// <summary>«Algete (ALG)»: desde donde sí se puede elegir otro modo.</summary>
        internal static string Algete()
            => $"{CalculadoraFechaEntregaAgencia.NombreAlmacen(Constantes.Almacenes.ALGETE)} ({Constantes.Almacenes.ALGETE})";

        /// <summary>«Según vaya entrando», el único modo de una tienda.</summary>
        internal static string ModoTienda()
            => Constantes.Pedidos.ModosServicio.Nombre(Constantes.Pedidos.ModosServicio.SEGUN_VAYA_ENTRANDO);

        /// <summary>«El pedido es de Alcobendas y allí solo puede salir «Según vaya entrando».»: el modo lo corrigió la API, no el usuario.</summary>
        internal static string ArranqueModoCorregido(string almacen)
            => $"El pedido es de {Sede(almacen)} y allí solo puede salir «{ModoTienda()}».";
    }
}
