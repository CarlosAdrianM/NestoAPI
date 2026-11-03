using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Productos
{
    /// <summary>
    /// NestoAPI#788 (épica #784): qué producto es lo que se ha leído o tecleado, con la misma regla que la recepción de
    /// reposiciones (<see cref="TraductorCodigosRecepcion"/>): primero la referencia (lo que se teclea; un código que además es
    /// la referencia de un producto se queda como referencia) y, si no, <see cref="ServicioCodigosBarras.BuscarPorCodigo"/>: la
    /// ficha, la tabla ProductosCodigosBarras y los internos del 29 (290…/299…), solo GTIN válidos y productos activos.
    /// A diferencia de la recepción, aquí no hay lista con la que desempatar: si el código lo llevan varios productos no se
    /// elige ninguno (se devuelven los candidatos).
    /// </summary>
    public class LectorProductoLeido
    {
        /// <summary>Productos.Número es char(15): algo más largo no puede ser una referencia.</summary>
        internal const int LONGITUD_MAXIMA_REFERENCIA = 15;

        private readonly IRepositorioCodigosBarras repositorio;
        private readonly ServicioCodigosBarras codigos;

        public LectorProductoLeido(IRepositorioCodigosBarras repositorio)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
            codigos = new ServicioCodigosBarras(repositorio);
        }

        public async Task<ResultadoProductoLeido> Leer(string empresa, string leido)
        {
            string limpio = ServicioCodigosBarras.Limpiar(leido);
            var resultado = new ResultadoProductoLeido { Leido = limpio };
            if (limpio == null)
            {
                return resultado;
            }

            if (limpio.Length <= LONGITUD_MAXIMA_REFERENCIA)
            {
                Producto porReferencia = await repositorio.LeerProducto(empresa, limpio).ConfigureAwait(false);
                if (porReferencia != null)
                {
                    resultado.Producto = porReferencia;
                    resultado.PorCodigoBarras = false;
                    return resultado;
                }
            }

            List<ProductoPorCodigoBarrasDTO> candidatos = await codigos.BuscarPorCodigo(empresa, limpio).ConfigureAwait(false)
                ?? new List<ProductoPorCodigoBarrasDTO>();
            if (candidatos.Count > 1)
            {
                resultado.Candidatos = candidatos;
                return resultado;
            }
            if (candidatos.Count == 1)
            {
                resultado.Producto = await repositorio.LeerProducto(empresa, candidatos[0].Producto).ConfigureAwait(false);
                resultado.PorCodigoBarras = resultado.Producto != null;
            }
            return resultado;
        }
    }

    public class ResultadoProductoLeido
    {
        /// <summary>Lo leído, sin espacios.</summary>
        public string Leido { get; set; }
        /// <summary>El producto, si casa con uno solo; null si no casa con ninguno o lo comparten varios.</summary>
        public Producto Producto { get; set; }
        /// <summary>True si casó por un código de barras; false si por la referencia (tecleada a mano).</summary>
        public bool PorCodigoBarras { get; set; }
        /// <summary>Los productos que comparten el código, si son varios (el principal primero).</summary>
        public List<ProductoPorCodigoBarrasDTO> Candidatos { get; set; } = new List<ProductoPorCodigoBarrasDTO>();

        public bool EsAmbiguo => Producto == null && Candidatos.Count > 1;

        public string MensajeAmbiguo => $"El código {Leido} lo llevan {Candidatos.Count} productos: lea o teclee la referencia del que está contando.";

        public string MensajeNoEncontrado => $"No hay ningún producto con la referencia o el código de barras {Leido}.";
    }
}
