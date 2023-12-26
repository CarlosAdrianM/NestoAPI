using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Contabilidad
{
    public class GestorContabilidad
    {
        private readonly IContabilidadService _servicio;

        public GestorContabilidad(IContabilidadService servicio)
        {
            this._servicio = servicio;
        }
        public async Task<int> CrearLineasDiario(List<PreContabilidad> lineas)
        {
            return await _servicio.CrearLineas(lineas);
        }

        public async Task<int> CrearLineasDiarioYContabilizar(List<PreContabilidad> lineas)
        {
            // test: si hay varios diarios en lineas hay que dar error
            // lo mismo si hay varias empresas

            return await _servicio.CrearLineasYContabilizarDiario(lineas);
        }
    }
}