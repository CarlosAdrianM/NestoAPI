using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Text;

namespace NestoAPI.Infraestructure.CorreosPostCompra
{
    /// <summary>
    /// NestoAPI#707: grupo control del correo post-compra. Un porcentaje pequeño de clientes (CorreosPostCompra:PorcentajeGrupoControl
    /// en Web.config; 0 = apagado, que es como nace) recibe el correo con lo que ha comprado pero SIN recomendados; los que habría
    /// visto se guardan igual en CorreosPostCompraRecomendaciones con GrupoControl = 1, para comparar después cuántos los compran
    /// sin habérselos enseñado (uplift).
    /// <para>Quién cae en el grupo no es aleatorio en cada envío: sale de un hash estable de empresa y cliente, así que un cliente
    /// está siempre en el mismo grupo mientras no cambie el porcentaje (y al subirlo, los que ya estaban siguen dentro).</para>
    /// </summary>
    public static class GrupoControlPostCompra
    {
        public const string CLAVE_PORCENTAJE = "CorreosPostCompra:PorcentajeGrupoControl";

        // Para que el reparto no coincida con el de otros experimentos que hagan hash del cliente
        private const string SAL = "NestoAPI#707";

        /// <summary>El porcentaje de Web.config, entre 0 y 100. Sin clave o con un valor que no es un número: 0 (apagado).</summary>
        public static int LeerPorcentaje() => Porcentaje(ConfigurationManager.AppSettings[CLAVE_PORCENTAJE]);

        internal static int Porcentaje(string valor)
            => int.TryParse(valor?.Trim(), out int porcentaje) ? Math.Max(0, Math.Min(100, porcentaje)) : 0;

        /// <summary>El cubo (0-99) del cliente: FNV-1a de 32 bits sobre «sal|empresa|cliente» (sin espacios ni mayúsculas).</summary>
        internal static int Cubo(string empresa, string cliente)
        {
            string clave = SAL + "|" + (empresa ?? string.Empty).Trim().ToUpperInvariant() + "|" + (cliente ?? string.Empty).Trim().ToUpperInvariant();
            unchecked
            {
                uint hash = 2166136261;
                foreach (byte b in Encoding.UTF8.GetBytes(clave))
                {
                    hash ^= b;
                    hash *= 16777619;
                }
                return (int)(hash % 100);
            }
        }

        /// <summary>Con 0 %, nadie; con 100 %, todos. Siempre el mismo resultado para el mismo cliente y porcentaje.</summary>
        public static bool EsGrupoControl(string empresa, string cliente, int porcentaje)
        {
            if (porcentaje <= 0 || string.IsNullOrWhiteSpace(cliente))
            {
                return false;
            }
            return porcentaje >= 100 || Cubo(empresa, cliente) < porcentaje;
        }

        /// <summary>
        /// Marca los correos de los clientes del grupo control y les quita los recomendados (pasan a ProductosRecomendadosNoMostrados).
        /// Con 0 % no toca nada. Devuelve cuántos han caído en el grupo.
        /// </summary>
        public static int Aplicar(IEnumerable<CorreoPostCompraClienteDTO> correos, int porcentaje)
        {
            if (correos == null || porcentaje <= 0)
            {
                return 0;
            }
            int enGrupo = 0;
            foreach (CorreoPostCompraClienteDTO correo in correos.Where(c => c != null))
            {
                if (!EsGrupoControl(correo.Empresa, correo.ClienteId, porcentaje))
                {
                    continue;
                }
                correo.GrupoControl = true;
                correo.ProductosRecomendadosNoMostrados = correo.ProductosRecomendados ?? new List<ProductoRecomendadoDTO>();
                correo.ProductosRecomendados = new List<ProductoRecomendadoDTO>();
                enGrupo++;
            }
            return enGrupo;
        }
    }
}
