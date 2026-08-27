using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Sincronizacion
{
    /// <summary>
    /// NestoAPI#406: pone el sentinel -1 (público = profesional) a los productos vivos de las
    /// familias marcadas con <c>PublicoIgualQueProfesional</c>.
    ///
    /// El problema que resuelve: al retirar las reglas de catálogo de PrestaShop, esa regla se
    /// quedó viviendo en un script de un día concreto que marcó una foto fija. Un producto NUEVO
    /// de Weelko, Staleks, Unión Láser, Fama Fabre o DDUUEETT nacía sin marcar y salía a la venta
    /// con un 42,86 % de más (el /0,7 del descuento por defecto), sin dar ningún error: solo un
    /// precio caro que nadie mira. Se crea aproximadamente uno cada tres días.
    ///
    /// POR QUÉ UN JOB Y NO UN TRIGGER NI EL ALTA DEL API: así no hay que saber por dónde entran
    /// los datos (Nesto, el API, la sincronización con Odoo, un INSERT a mano), solo qué estado
    /// final deben tener. Es idempotente, cubre las altas por cualquier vía y de paso repesca los
    /// olvidos históricos. Que tarde unas horas da igual: entre que se crea un producto y se
    /// publica en la tienda pasa bastante más que eso.
    /// </summary>
    public class SentinelPrecioPublicoJobsService
    {
        internal const string USUARIO = "Sentinel familia";

        /// <summary>Punto de entrada para Hangfire (job recurrente).</summary>
        public static async Task MarcarProductosDeFamiliasConPublicoIgual()
        {
            Console.WriteLine("🚀 [Hangfire] Marcando productos con público = profesional...");

            using (NVEntities db = new NVEntities())
            {
                int marcados = await Marcar(db).ConfigureAwait(false);
                Console.WriteLine($"✅ [Hangfire] Sentinel de precio público: {marcados} producto(s) marcado(s)");
            }
        }

        // Internal para tests (InternalsVisibleTo("NestoAPI.Tests")).
        internal static async Task<int> Marcar(NVEntities db)
        {
            List<string> familias = await db.Familias
                .Where(f => f.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && f.PublicoIgualQueProfesional)
                .Select(f => f.Número)
                .ToListAsync()
                .ConfigureAwait(false);

            // Los char vienen con relleno de la base de datos y hay que compararlos recortados:
            // en SQL da igual, pero si algún día esto se filtra en memoria, "Fama" y "Fama      "
            // dejarían de ser lo mismo sin dar ningún error.
            HashSet<string> codigos = new HashSet<string>(familias.Select(f => f.Trim()));

            if (!codigos.Any())
            {
                return 0;
            }

            List<string> productos = await db.Productos
                .Where(p => p.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO
                         && p.Estado >= 0
                         && codigos.Contains(p.Familia.Trim()))
                .Select(p => p.Número)
                .ToListAsync()
                .ConfigureAwait(false);

            if (!productos.Any())
            {
                return 0;
            }

            List<PrestashopProducto> fichas = await db.PrestashopProductos
                .Where(pp => pp.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && productos.Contains(pp.Número))
                .ToListAsync()
                .ConfigureAwait(false);

            Dictionary<string, PrestashopProducto> fichasPorProducto = fichas
                .GroupBy(pp => pp.Número.Trim())
                .ToDictionary(g => g.Key, g => g.First());

            List<string> tocados = new List<string>();

            foreach (string producto in productos.Select(p => p.Trim()))
            {
                if (fichasPorProducto.TryGetValue(producto, out PrestashopProducto ficha))
                {
                    // Solo se rellena el hueco. Un precio público FIJO (positivo) es una decisión
                    // deliberada de alguien para ese producto concreto y gana a la regla de la
                    // familia: pisarlo con el sentinel le cambiaría el precio a la web sin que
                    // nadie lo haya pedido. Y si ya está a -1, no hay nada que hacer.
                    if (ficha.PVP_IVA_Incluido != null)
                    {
                        continue;
                    }

                    ficha.PVP_IVA_Incluido = Constantes.Productos.PVP_IVA_MISMO_QUE_PROFESIONAL;
                    ficha.Usuario = USUARIO;
                    ficha.Fecha_Modificación = DateTime.Now;
                }
                else
                {
                    // Sin ficha de tienda: se crea con lo mínimo. Son productos que aún no se han
                    // creado en la web (normalmente porque nunca han tenido stock), y así nacen ya
                    // con el precio bueno en vez de esperar a que alguien se acuerde.
                    _ = db.PrestashopProductos.Add(new PrestashopProducto
                    {
                        Empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO,
                        Número = producto,
                        PVP_IVA_Incluido = Constantes.Productos.PVP_IVA_MISMO_QUE_PROFESIONAL,
                        Usuario = USUARIO,
                        Fecha_Modificación = DateTime.Now
                    });
                }

                tocados.Add(producto);
            }

            if (!tocados.Any())
            {
                return 0;
            }

            _ = await db.SaveChangesAsync().ConfigureAwait(false);

            // Republicar: el precio que la tienda tiene publicado de estos productos es el inflado.
            // Se encola DESPUÉS de guardar, para no avisar de un cambio que no llegó a cuajar.
            foreach (string producto in tocados)
            {
                _ = await db.EncolarProductoSync(producto, USUARIO).ConfigureAwait(false);
            }

            return tocados.Count;
        }
    }
}
