using NestoAPI.Controllers;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.ValidadoresPedido
{
    /// <summary>
    /// LA REGLA (pura) de qué filas de DescuentosProducto pueden AUTORIZAR el descuento de un producto en la validación del pedido
    /// (Carlos 25/11/26). Hasta hoy <see cref="ServicioPrecios.BuscarDescuentosPermitidos"/> traía cualquier fila con la misma familia
    /// O el mismo grupo, así que las 37 filas del Outlet (Familia + Grupo + Subgrupo, p. ej. Schwarzkop | PEL | OUT al 50 %) autorizaban
    /// el 50 % en CUALQUIER producto de PEL y el 40 % en cualquiera de COS (695 líneas del 15/09 al 24/11 sin fila que lo autorizara).
    /// <para>Ahora una fila autoriza solo con los MISMOS niveles que aplica el motor (<see cref="GestorPrecios.calcularDescuentoProducto"/>):</para>
    /// <list type="bullet">
    /// <item>Producto (global o del cliente): <c>Nº_Producto</c> = el producto.</item>
    /// <item>Global de familia: familia = la del producto, sin filtro; con grupo y sin subgrupo, el grupo de la ficha; con grupo y subgrupo
    /// (NestoAPI#467, el Outlet), el par grupo/subgrupo contra la categoría principal O una secundaria (ProductosCategoriasSecundarias).</item>
    /// <item>Global SIN familia (solo grupo, o nada): NO autoriza (Carlos 25/11/26: «para que el descuento por grupo aplique tiene que ser
    /// una fila con ese cliente, con ese grupo y el producto a NULL»). Tampoco una global de familia con filtro (el motor no tiene ese nivel).</item>
    /// <item>Del cliente sin familia: con grupo = el grupo de la ficha.</item>
    /// <item>Del cliente con familia = la del producto: con grupo, el de la ficha; sin grupo y con filtro, el nombre empieza por él;
    /// sin grupo ni filtro, toda la familia.</item>
    /// </list>
    /// El contacto de las filas del cliente se sigue filtrando aparte (<see cref="ServicioPrecios.FiltroClienteContacto"/>), y la vigencia
    /// también; la cantidad mínima la mira el validador.
    /// </summary>
    public static class ReglaFilaAutorizaProducto
    {
        public static bool Casa(DescuentosProducto fila, Producto producto, IEnumerable<CategoriaProducto> categoriasSecundarias, string cliente)
        {
            if (fila == null || producto == null || Limpio(fila.NºProveedor) != null)
            {
                return false;
            }
            string filaCliente = Limpio(fila.Nº_Cliente);
            if (filaCliente != null && !Igual(filaCliente, cliente))
            {
                return false;
            }
            string filaProducto = Limpio(fila.Nº_Producto);
            if (filaProducto != null)
            {
                return Igual(filaProducto, producto.Número);
            }
            string filaFamilia = Limpio(fila.Familia);
            string filaGrupo = Limpio(fila.GrupoProducto);
            string filaSubgrupo = Limpio(fila.SubGrupoProducto);
            string filtro = Limpio(fila.FiltroProducto);

            if (filaCliente == null)
            {
                if (filaFamilia == null || !Igual(filaFamilia, producto.Familia) || filtro != null)
                {
                    return false;
                }
                if (filaGrupo == null)
                {
                    return true;
                }
                if (filaSubgrupo == null)
                {
                    return Igual(filaGrupo, producto.Grupo);
                }
                List<CategoriaProducto> categorias = new List<CategoriaProducto> { new CategoriaProducto(producto.Grupo, producto.SubGrupo) };
                categorias.AddRange((categoriasSecundarias ?? Enumerable.Empty<CategoriaProducto>()).Where(c => c != null));
                return categorias.Any(c => Igual(filaGrupo, c.Grupo) && Igual(filaSubgrupo, c.SubGrupo));
            }

            if (filaFamilia == null)
            {
                return filaGrupo != null && Igual(filaGrupo, producto.Grupo);
            }
            if (!Igual(filaFamilia, producto.Familia))
            {
                return false;
            }
            if (filaGrupo != null)
            {
                return Igual(filaGrupo, producto.Grupo);
            }
            return filtro == null
                || (producto.Nombre != null && producto.Nombre.StartsWith(filtro, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Las filas (ya leídas con la consulta amplia) que autorizan de verdad el producto.</summary>
        public static List<DescuentosProducto> Filtrar(IEnumerable<DescuentosProducto> filas, Producto producto, IEnumerable<CategoriaProducto> categoriasSecundarias, string cliente)
        {
            List<CategoriaProducto> categorias = (categoriasSecundarias ?? Enumerable.Empty<CategoriaProducto>()).ToList();
            return (filas ?? Enumerable.Empty<DescuentosProducto>()).Where(f => Casa(f, producto, categorias, cliente)).ToList();
        }

        private static string Limpio(string texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();

        private static bool Igual(string a, string b) => string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Interruptor por DATO de <see cref="ReglaFilaAutorizaProducto"/>: ParametrosUsuario empresa 1 «(defecto)»
    /// <c>Validacion.DescuentosCriteriosEstrictos</c>. Sin fila o «1» = la regla nueva; «0» (o false/no/off/apagado) = la consulta laxa de
    /// antes. Se lee sin crear filas y con caché de 1 minuto; si no se puede leer, la regla nueva.
    /// </summary>
    public static class InterruptorDescuentosCriteriosEstrictos
    {
        public const string CLAVE = "Validacion.DescuentosCriteriosEstrictos";
        internal const string USUARIO_DEFECTO = "(defecto)";
        internal static readonly TimeSpan CACHE = TimeSpan.FromMinutes(1);

        private static readonly object candado = new object();
        private static bool? valor;
        private static DateTime leidoEl;

        /// <summary>Para los tests: null = leer ParametrosUsuario.</summary>
        internal static Func<bool> Forzado { get; set; }

        public static bool Activo
        {
            get
            {
                if (Forzado != null)
                {
                    return Forzado();
                }
                lock (candado)
                {
                    if (valor.HasValue && DateTime.UtcNow - leidoEl < CACHE)
                    {
                        return valor.Value;
                    }
                    bool leido;
                    try
                    {
                        leido = Interpretar(ParametrosUsuarioController.LeerParametroSinDefecto(Constantes.Empresas.EMPRESA_POR_DEFECTO, USUARIO_DEFECTO, CLAVE));
                    }
                    catch (Exception ex)
                    {
                        ElmahHelper.Log(new Exception($"[Validación de descuentos] No se ha podido leer {CLAVE}: se usa la regla estricta. {ex.Message}", ex));
                        leido = true;
                    }
                    valor = leido;
                    leidoEl = DateTime.UtcNow;
                    return leido;
                }
            }
        }

        /// <summary>Encendido salvo «0», «false», «no», «off» o «apagado» (vacío o sin fila = encendido).</summary>
        internal static bool Interpretar(string texto)
        {
            string limpio = texto?.Trim().ToLowerInvariant();
            return !(limpio == "0" || limpio == "false" || limpio == "no" || limpio == "off" || limpio == "apagado");
        }

        internal static void Reiniciar()
        {
            lock (candado)
            {
                valor = null;
                Forzado = null;
            }
        }
    }
}
