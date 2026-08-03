using NestoAPI.Models;
using NestoAPI.Models.CodigosPostales;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.CodigosPostales
{
    /// <summary>
    /// #378: mantenimiento de códigos postales (ventana de Nesto para Dirección y Tienda online).
    /// Permite poner bien el país de los CPs que se sigan creando sin él desde Nesto viejo y
    /// editar población, provincia, ruta, vendedor y los vendedores por grupo de producto.
    /// </summary>
    public class GestorMantenimientoCodigosPostales
    {
        private const int TOPE_RESULTADOS = 50;
        private readonly NVEntities db;

        public GestorMantenimientoCodigosPostales(NVEntities db)
        {
            this.db = db;
        }

        public async Task<List<CodigoPostalMantenimientoDTO>> Buscar(string empresa, string filtro)
        {
            empresa = (string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa).Trim();
            filtro = filtro?.Trim().ToUpper();
            if (string.IsNullOrEmpty(filtro))
            {
                return new List<CodigoPostalMantenimientoDTO>();
            }

            List<CodigoPostal> cps = await db.CodigosPostales
                .Where(c => c.Empresa == empresa && (c.Número.StartsWith(filtro) || c.Descripción.Contains(filtro)))
                .OrderBy(c => c.Número)
                .Take(TOPE_RESULTADOS)
                .ToListAsync().ConfigureAwait(false);

            List<string> numeros = cps.Select(c => c.Número).ToList();
            List<VendedorCodigoPostalGrupoProducto> vendedoresGrupo = await db.VendedoresCodigoPostalGruposProductos
                .Where(v => v.Empresa == empresa && numeros.Contains(v.CodigoPostal))
                .ToListAsync().ConfigureAwait(false);

            return cps.Select(c => ADto(c, vendedoresGrupo.Where(v => v.CodigoPostal.Trim() == c.Número.Trim()))).ToList();
        }

        /// <summary>Actualiza el CP y sincroniza sus vendedores por grupo de producto. Null si el CP no existe.</summary>
        public async Task<CodigoPostalMantenimientoDTO> Actualizar(CodigoPostalMantenimientoDTO dto, string usuario)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.Numero))
            {
                throw new ArgumentException("Falta el número del código postal");
            }
            string empresa = (string.IsNullOrWhiteSpace(dto.Empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : dto.Empresa).Trim();
            string numero = dto.Numero.Trim();

            CodigoPostal cp = await db.CodigosPostales
                .SingleOrDefaultAsync(c => c.Empresa == empresa && c.Número == numero)
                .ConfigureAwait(false);
            if (cp == null)
            {
                return null;
            }

            // Provincia, Ruta y Vendedor son NOT NULL en BD: si vienen vacíos se conserva lo que había
            cp.Descripción = Truncar(dto.Poblacion?.Trim().ToUpper(), 50) ?? cp.Descripción;
            cp.Provincia = Truncar(VacioANull(dto.Provincia)?.ToUpper(), 30) ?? cp.Provincia;
            cp.Ruta = VacioANull(dto.Ruta) ?? cp.Ruta;
            cp.Vendedor = VacioANull(dto.Vendedor) ?? cp.Vendedor;
            cp.Pais = VacioANull(dto.Pais)?.ToUpper();

            List<VendedorCodigoPostalGrupoProducto> existentes = await db.VendedoresCodigoPostalGruposProductos
                .Where(v => v.Empresa == empresa && v.CodigoPostal == numero)
                .ToListAsync().ConfigureAwait(false);
            List<VendedorGrupoProductoCodigoPostalDTO> deseados = (dto.VendedoresGrupoProducto ?? new List<VendedorGrupoProductoCodigoPostalDTO>())
                .Where(d => !string.IsNullOrWhiteSpace(d.GrupoProducto) && !string.IsNullOrWhiteSpace(d.Vendedor))
                .GroupBy(d => d.GrupoProducto.Trim())
                .Select(g => g.First())
                .ToList();

            foreach (VendedorCodigoPostalGrupoProducto sobrante in existentes
                .Where(e => !deseados.Any(d => d.GrupoProducto.Trim() == e.GrupoProducto.Trim())))
            {
                _ = db.VendedoresCodigoPostalGruposProductos.Remove(sobrante);
            }
            foreach (VendedorGrupoProductoCodigoPostalDTO deseado in deseados)
            {
                VendedorCodigoPostalGrupoProducto existente = existentes
                    .FirstOrDefault(e => e.GrupoProducto.Trim() == deseado.GrupoProducto.Trim());
                if (existente == null)
                {
                    _ = db.VendedoresCodigoPostalGruposProductos.Add(new VendedorCodigoPostalGrupoProducto
                    {
                        Empresa = empresa,
                        CodigoPostal = numero,
                        GrupoProducto = deseado.GrupoProducto.Trim(),
                        Vendedor = deseado.Vendedor.Trim(),
                        Usuario = usuario,
                        FechaModificacion = DateTime.Now
                    });
                }
                else if (existente.Vendedor.Trim() != deseado.Vendedor.Trim())
                {
                    existente.Vendedor = deseado.Vendedor.Trim();
                    existente.Usuario = usuario;
                    existente.FechaModificacion = DateTime.Now;
                }
            }

            _ = await db.SaveChangesAsync().ConfigureAwait(false);

            List<VendedorCodigoPostalGrupoProducto> actuales = existentes
                .Where(e => deseados.Any(d => d.GrupoProducto.Trim() == e.GrupoProducto.Trim()))
                .ToList();
            return ADto(cp, actuales.Concat(deseados
                .Where(d => !actuales.Any(a => a.GrupoProducto.Trim() == d.GrupoProducto.Trim()))
                .Select(d => new VendedorCodigoPostalGrupoProducto
                {
                    GrupoProducto = d.GrupoProducto,
                    Vendedor = d.Vendedor,
                    CodigoPostal = numero
                })));
        }

        private static CodigoPostalMantenimientoDTO ADto(CodigoPostal cp, IEnumerable<VendedorCodigoPostalGrupoProducto> vendedoresGrupo)
            => new CodigoPostalMantenimientoDTO
            {
                Empresa = cp.Empresa?.Trim(),
                Numero = cp.Número?.Trim(),
                Poblacion = cp.Descripción?.Trim(),
                Provincia = cp.Provincia?.Trim(),
                Ruta = cp.Ruta?.Trim(),
                Vendedor = cp.Vendedor?.Trim(),
                Pais = cp.Pais?.Trim(),
                VendedoresGrupoProducto = vendedoresGrupo
                    .OrderBy(v => v.GrupoProducto)
                    .Select(v => new VendedorGrupoProductoCodigoPostalDTO
                    {
                        GrupoProducto = v.GrupoProducto?.Trim(),
                        Vendedor = v.Vendedor?.Trim()
                    })
                    .ToList()
            };

        private static string VacioANull(string texto)
            => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();

        private static string Truncar(string texto, int longitud)
            => texto != null && texto.Length > longitud ? texto.Substring(0, longitud) : texto;
    }
}
