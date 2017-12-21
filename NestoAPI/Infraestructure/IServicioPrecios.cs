﻿using NestoAPI.Models;
using System.Collections.Generic;

namespace NestoAPI.Infraestructure
{
    public interface IServicioPrecios
    {
        Producto BuscarProducto(string producto);
        List<OfertaPermitida> BuscarOfertasPermitidas(string producto);
        List<DescuentosProducto> BuscarDescuentosPermitidos(string numeroProducto, string numeroCliente, string contactoCliente);
    }
}