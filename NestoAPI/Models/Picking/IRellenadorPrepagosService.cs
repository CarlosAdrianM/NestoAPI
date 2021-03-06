﻿using System.Collections.Generic;

namespace NestoAPI.Models.Picking
{
    public interface IRellenadorPrepagosService
    {
        List<PrepagoDTO> Prepagos(int pedido);
        List<ExtractoClienteDTO> ExtractosPendientes(int pedido);
    }
}
