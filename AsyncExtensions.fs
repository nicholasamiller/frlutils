module Result

/// functions for AsyncResult
  
  // takes a regular result and returns an async result
    let bindAsync asyncFunctionThatReturnsAsyncResult result  = async {
        match result with
        | Ok x -> 
            let! newResult = asyncFunctionThatReturnsAsyncResult x
            return newResult
        | Error err -> return Error err
    }
