using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Vendedores
{
    public class ServicioVendedores : IServicioVendedores
    {
        public DateTime Fecha { get; set; } = DateTime.Today; //new DateTime(2024, 1, 1); 
        public async Task<List<VendedorDTO>> VendedoresEquipo(string empresa, string vendedor)
        {
            using (NVEntities db = new NVEntities())
            {
                if (string.IsNullOrEmpty(empresa))
                {
                    throw new ArgumentNullException("La empresa no puede ser nula");
                }
                var vendedoresEquipo = await db.EquiposVentas
                    .Where(v => v.Empresa.Trim() == empresa.Trim() && v.Superior.Trim().ToLower() == vendedor.Trim().ToLower() && (v.FechaDesde == null || v.FechaDesde <= Fecha) && (v.FechaHasta == null || v.FechaHasta >= Fecha))
                    .ToListAsync()
                    .ConfigureAwait(false);

                List<VendedorDTO> vendedores;
                var vendedoresEquipoNumeros = vendedoresEquipo.Select(e => e.Vendedor.Trim().ToLower()).ToList();

                vendedores = await db.Vendedores
                    .Where(v => v.Empresa.Trim() == empresa.Trim() && vendedoresEquipoNumeros.Contains(v.Número.Trim().ToLower()))
                    .Select(p => new VendedorDTO
                    {
                        vendedor = p.Número.Trim(),
                        nombre = p.Descripción.Trim(),
                        estado = (int)p.Estado
                    })
                    .OrderBy(v => v.vendedor)
                    .ToListAsync()
                    .ConfigureAwait(false);

                vendedores.AddRange(
                    await db.Vendedores.Where(v => v.Empresa.Trim() == empresa.Trim() && v.Número.Trim().ToLower() == vendedor.Trim().ToLower())
                    .Select(p => new VendedorDTO
                    {
                        vendedor = p.Número.Trim(),
                        nombre = p.Descripción.Trim(),
                        estado = (int)p.Estado
                    })
                    .ToListAsync()
                    .ConfigureAwait(false)
                );

                return vendedores;
            }
        }
    }
}