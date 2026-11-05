using NestoAPI.Models;
using System.Linq;

namespace NestoAPI.Infraestructure.PreparacionAlmacen
{
    /// <summary>
    /// NestoAPI#825 (sugerencia 1056, Santiago; Carlos 05/11/26): «En los clientes que usan factura simplificada, cada pedido puede
    /// ser para un cliente final diferente (de hecho las etiquetas de agencia son para destinos diferentes). Tenemos que manejarlos
    /// como tal». La regla ÚNICA de qué pedidos van solos (su propia entrega, su bloque del packing en papel, sus cajas):
    /// <list type="bullet">
    /// <item>Los de un cliente ficticio de factura simplificada (<see cref="Constantes.ClientesEspeciales.EsClienteFacturaSimplificada"/>,
    /// la misma lista que Verifactu usa para las F2): Amazon 32624, tienda online 31517 (PrestaShop y Miravia sin ficha), venta en
    /// tienda 10458, pedido tienda 9500 y materiales de cursos 31794.</item>
    /// <item>Los canales externos (Amazon, Miravia, PrestaShop) no tienen marca propia en el pedido: el que no casa con una ficha real
    /// (por teléfono) entra con 32624 o 31517, así que queda cubierto; el que casa con una ficha real es de ese cliente y se agrupa
    /// como los demás.</item>
    /// <item>Los clientes reales, como siempre: mismo cliente y contacto (misma dirección) = misma entrega.</item>
    /// </list>
    /// </summary>
    public static class PedidosQueVanSolos
    {
        /// <summary>¿Los pedidos de este cliente van cada uno solo, aunque coincidan cliente y contacto?</summary>
        public static bool PedidoVaSolo(string cliente) => Constantes.ClientesEspeciales.EsClienteFacturaSimplificada(cliente);

        public static bool PedidoVaSolo(CabPedidoVta pedido) => pedido != null && PedidoVaSolo(pedido.Nº_Cliente);

        /// <summary>
        /// Lo que se añade a cliente + contacto para formar la entrega: el número de pedido si va solo y 0 si se agrupa (así dos
        /// pedidos de un cliente real a la misma dirección siguen en la misma entrega).
        /// </summary>
        public static int ClaveSolo(string cliente, int pedido) => PedidoVaSolo(cliente) ? pedido : 0;

        /// <summary>
        /// La misma regla en SQL, para las consultas que agrupan entregas en la BD: <c>RTRIM(columna) IN ('10458', …)</c>, sacada de la
        /// lista de <see cref="Constantes.ClientesEspeciales"/> (son constantes del código, no entrada del usuario).
        /// </summary>
        public static string CondicionSql(string columnaCliente)
            => "RTRIM(" + columnaCliente + ") IN (" + string.Join(", ",
                Constantes.ClientesEspeciales.ClientesFacturaSimplificada.OrderBy(c => c).Select(c => "'" + c.Replace("'", "''") + "'")) + ")";

        /// <summary>La clave de <see cref="ClaveSolo"/> en SQL: <c>CASE WHEN cliente IN (…) THEN pedido ELSE 0 END</c>.</summary>
        public static string ClaveSoloSql(string columnaCliente, string columnaPedido)
            => "CASE WHEN " + CondicionSql(columnaCliente) + " THEN " + columnaPedido + " ELSE 0 END";
    }
}
