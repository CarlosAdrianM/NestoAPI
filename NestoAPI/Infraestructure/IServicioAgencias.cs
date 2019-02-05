﻿using NestoAPI.Models;
using System;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure
{
    public interface IServicioAgencias
    {
        string LeerCodigoPostal(PedidoVentaDTO pedido);
        Task<RespuestaAgencia> LeerDireccionGoogleMaps(PedidoVentaDTO pedido);
        DateTime HoraActual();
    }
}